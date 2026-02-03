using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.CreateDevice;

public sealed record CreateDeviceCommand(
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod,
    string? SerialNumber = null) : IRequest<Result<Guid>>;

public sealed class CreateDeviceCommandHandler : IRequestHandler<CreateDeviceCommand, Result<Guid>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IZKTecoDeviceClient _deviceClient;
    private readonly ILogger<CreateDeviceCommandHandler> _logger;

    public CreateDeviceCommandHandler(
        IDeviceRepository deviceRepository, 
        IUnitOfWork unitOfWork,
        IZKTecoDeviceClient deviceClient,
        ILogger<CreateDeviceCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _deviceClient = deviceClient;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(CreateDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = Device.Create(
            Guid.NewGuid().ToString(),
            request.Name,
            request.IpAddress,
            request.Port,
            request.Location,
            request.ShouldClearAfterDownload,
            request.DownloadMethod,
            request.SerialNumber);

        // Intentar conectar y obtener información del dispositivo solo si es SDK
        if (request.DownloadMethod == DeviceDownloadMethod.Sdk)
        {
            try
            {
                _logger.LogInformation("Conectando al dispositivo {IpAddress}:{Port} para obtener información...", 
                    request.IpAddress, request.Port);

                var connected = await _deviceClient.ConnectAsync(request.IpAddress, request.Port, cancellationToken);
                
                if (connected)
                {
                    var deviceInfo = await _deviceClient.GetDeviceInfoAsync(cancellationToken);
                    
                    if (deviceInfo != null)
                    {
                        var hardwareInfo = new DeviceHardwareInfo(
                            deviceInfo.SerialNumber,
                            deviceInfo.FirmwareVersion,
                            deviceInfo.Platform,
                            deviceInfo.UserCount,
                            deviceInfo.FingerprintCount,
                            deviceInfo.FaceCount,
                            deviceInfo.AttendanceRecordCount,
                            deviceInfo.UserCapacity,
                            deviceInfo.FingerprintCapacity,
                            deviceInfo.FaceCapacity,
                            deviceInfo.AttendanceRecordCapacity);

                        device.UpdateDeviceInfo(hardwareInfo);
                        
                        _logger.LogInformation("Información del dispositivo obtenida: S/N={SerialNumber}", 
                            deviceInfo.SerialNumber);
                    }

                    await _deviceClient.DisconnectAsync(cancellationToken);
                }
                else
                {
                    _logger.LogWarning("No se pudo conectar al dispositivo, se guardará sin información adicional");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al obtener información del dispositivo, se guardará sin información adicional");
            }
        }

        await _deviceRepository.AddAsync(device, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(Guid.Parse(device.Id.Value));
    }
}
