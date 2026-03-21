using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Commands.RefreshDeviceInfo;

public record RefreshDeviceInfoCommand(Guid DeviceId) : IRequest<Result>;

public class RefreshDeviceInfoCommandHandler : IRequestHandler<RefreshDeviceInfoCommand, Result>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshDeviceInfoCommandHandler(
        IDeviceRepository deviceRepository,
        IDeviceClientFactory deviceClientFactory,
        IUnitOfWork unitOfWork)
    {
        _deviceRepository = deviceRepository;
        _deviceClientFactory = deviceClientFactory;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RefreshDeviceInfoCommand request, CancellationToken cancellationToken)
    {
        var deviceId = DeviceId.From(request.DeviceId.ToString());
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure("Dispositivo no encontrado");
        }

        // 0. Obtener cliente específico
        var deviceClient = _deviceClientFactory.GetClient(device);

        // 1. Conectar
        var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
        if (!connected)
        {
            return Result.Failure($"No se pudo conectar a {device.IpAddress}");
        }

        try
        {
            // 2. Obtener Info
            var info = await deviceClient.GetDeviceInfoAsync(cancellationToken);
            if (info is null)
            {
                return Result.Failure("No se pudo obtener la información del dispositivo");
            }

            var hardwareInfo = new DeviceHardwareInfo(
                info.SerialNumber,
                info.FirmwareVersion,
                info.Platform,
                info.UserCount,
                info.FingerprintCount,
                info.FaceCount,
                info.AttendanceRecordCount,
                info.UserCapacity,
                info.FingerprintCapacity,
                info.FaceCapacity,
                info.AttendanceRecordCapacity);

            // 3. Actualizar entidad
            device.UpdateDeviceInfo(hardwareInfo);

            await _deviceRepository.UpdateAsync(device, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        finally
        {
            // 4. Desconectar
            await deviceClient.DisconnectAsync(cancellationToken);
        }
    }
}
