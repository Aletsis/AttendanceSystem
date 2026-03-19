using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Enumerations;

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
    DeviceBrand Brand,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod,
    string? SerialNumber = null,
    string? Username = null,
    string? Password = null) : IRequest<Result<Guid>>;

public sealed class CreateDeviceCommandHandler : IRequestHandler<CreateDeviceCommand, Result<Guid>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly ILogger<CreateDeviceCommandHandler> _logger;

    public CreateDeviceCommandHandler(
        IDeviceRepository deviceRepository, 
        IUnitOfWork unitOfWork,
        IDeviceClientFactory deviceClientFactory,
        ILogger<CreateDeviceCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _deviceClientFactory = deviceClientFactory;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(CreateDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = Device.Create(
            Guid.NewGuid().ToString(),
            request.Name,
            request.IpAddress,
            request.Port,
            request.Brand,
            request.Location,
            request.ShouldClearAfterDownload,
            request.DownloadMethod,
            request.SerialNumber,
            request.Username,
            request.Password);

        // Intentar conectar y obtener información del dispositivo solo si es SDK
        if (request.DownloadMethod == DeviceDownloadMethod.Sdk)
        {
            try
            {
                _logger.LogInformation("Conectando al dispositivo {Brand} {IpAddress}:{Port} para obtener información...", 
                    request.Brand, request.IpAddress, request.Port);

                var deviceClient = _deviceClientFactory.GetClient(request.Brand);
                var connected = await deviceClient.ConnectAsync(request.IpAddress, request.Port, request.Username, request.Password, cancellationToken);
                
                if (connected)
                {
                    var deviceInfo = await deviceClient.GetDeviceInfoAsync(cancellationToken);
                    
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

                    await deviceClient.DisconnectAsync(cancellationToken);
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
