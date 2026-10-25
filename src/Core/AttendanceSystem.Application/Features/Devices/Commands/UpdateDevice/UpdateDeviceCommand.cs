using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Commands.UpdateDevice;

[RequiresPermission(Permissions.DevicesManage)]
public sealed record UpdateDeviceCommand(
    Guid Id,
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    DeviceBrand Brand,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod,
    string? SerialNumber = null,
    string? Username = null,
    string? Password = null,
    string? TimeZoneId = null,
    // Caso 18 (R1): fecha mínima aceptada de sus marcajes; null = alta del reloj menos los días configurados.
    DateTime? AcceptPunchesFrom = null,
    bool UpdateAcceptPunchesFrom = false) : IRequest<Result>;

public sealed class UpdateDeviceCommandHandler : IRequestHandler<UpdateDeviceCommand, Result>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Microsoft.Extensions.Logging.ILogger<UpdateDeviceCommandHandler> _logger;
    private readonly ISender? _sender;

    public UpdateDeviceCommandHandler(
        IDeviceRepository deviceRepository,
        IUnitOfWork unitOfWork,
        Microsoft.Extensions.Logging.ILogger<UpdateDeviceCommandHandler> logger,
        ISender? sender = null)
    {
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _sender = sender;
    }

    public async Task<Result> Handle(UpdateDeviceCommand request, CancellationToken cancellationToken)
    {
        var deviceId = DeviceId.From(request.Id.ToString());
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure("El dispositivo no fue encontrado");
        }

        var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim();
        if (timeZoneId != null && !AttendanceSystem.Application.Common.TimeZoneCatalog.IsValid(timeZoneId))
        {
            return Result.Failure($"Zona horaria desconocida: {timeZoneId}");
        }
        var timeZoneChanged = timeZoneId != null && timeZoneId != device.TimeZoneId;
        var previousTimeZone = device.TimeZoneId;

        _logger.LogInformation("Actualizando dispositivo {Id} ({Name}). Nuevo SN recibido: '{SerialNumber}'",
            request.Id, request.Name, request.SerialNumber);

        device.UpdateConfiguration(
            request.Name,
            request.IpAddress,
            request.Port,
            request.Brand,
            request.Location,
            request.ShouldClearAfterDownload,
            request.DownloadMethod,
            request.SerialNumber,
            request.Username,
            request.Password,
            timeZoneId);

        if (request.UpdateAcceptPunchesFrom) device.SetAcceptPunchesFrom(request.AcceptPunchesFrom);

        _logger.LogInformation("Dispositivo actualizado en memoria. SN actual en entidad: '{SerialNumber}'",
            device.HardwareInfo.SerialNumber);

        await _deviceRepository.UpdateAsync(device, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (timeZoneChanged)
        {
            // Los marcajes ya guardados conservan su instante y su zona; sólo los nuevos se interpretan con la nueva.
            _logger.LogWarning("Zona horaria del reloj {Name} cambiada de {Previous} a {Current}; se sincroniza su hora.",
                device.Name, previousTimeZone ?? "(sistema)", timeZoneId);
            if (_sender != null)
            {
                var sync = await _sender.Send(new AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime.SetDeviceTimeCommand(device.Id.Value), cancellationToken);
                if (sync.IsFailure)
                {
                    _logger.LogWarning("No se pudo sincronizar el reloj {Name} tras el cambio de zona: {Error}. Sincronícelo desde sus detalles.", device.Name, sync.Error);
                }
            }
        }

        return Result.Success();
    }
}
