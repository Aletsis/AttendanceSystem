using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Commands.UpdateDevice;

public sealed record UpdateDeviceCommand(
    Guid Id,
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod,
    string? SerialNumber = null) : IRequest<Result>;

public sealed class UpdateDeviceCommandHandler : IRequestHandler<UpdateDeviceCommand, Result>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Microsoft.Extensions.Logging.ILogger<UpdateDeviceCommandHandler> _logger;

    public UpdateDeviceCommandHandler(
        IDeviceRepository deviceRepository, 
        IUnitOfWork unitOfWork,
        Microsoft.Extensions.Logging.ILogger<UpdateDeviceCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(UpdateDeviceCommand request, CancellationToken cancellationToken)
    {
        var deviceId = DeviceId.From(request.Id.ToString());
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure("El dispositivo no fue encontrado");
        }

        _logger.LogInformation("Actualizando dispositivo {Id} ({Name}). Nuevo SN recibido: '{SerialNumber}'", 
            request.Id, request.Name, request.SerialNumber);

        device.UpdateConfiguration(
            request.Name,
            request.IpAddress,
            request.Port,
            request.Location,
            request.ShouldClearAfterDownload,
            request.DownloadMethod,
            request.SerialNumber);

        _logger.LogInformation("Dispositivo actualizado en memoria. SN actual en entidad: '{SerialNumber}'", 
            device.HardwareInfo.SerialNumber);

        await _deviceRepository.UpdateAsync(device, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
