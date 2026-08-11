using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth;

public sealed record MonitorDeviceHealthCommand() : IRequest<Result<int>>;

public sealed class MonitorDeviceHealthCommandHandler : IRequestHandler<MonitorDeviceHealthCommand, Result<int>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IEmailService _emailService;
    private readonly IMediator _mediator;
    private readonly ILogger<MonitorDeviceHealthCommandHandler> _logger;

    public MonitorDeviceHealthCommandHandler(
        IDeviceRepository deviceRepository,
        IDeviceClientFactory deviceClientFactory,
        IEmailService emailService,
        IMediator mediator,
        ILogger<MonitorDeviceHealthCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _deviceClientFactory = deviceClientFactory;
        _emailService = emailService;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(MonitorDeviceHealthCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var configResult = await _mediator.Send(new GetSystemConfigurationQuery(), cancellationToken);
            var config = configResult.Value;

            if (config == null || !config.AreAlertsEnabled)
            {
                return Result<int>.Success(0);
            }

            var devices = await _deviceRepository.GetAllDevicesAsync(cancellationToken);
            int devicesChecked = 0;

            foreach (var device in devices)
            {
                // Only monitor active SDK devices (ADMS devices push to server, harder to ping directly unless IP is known and reachable)
                if (!device.IsActive || device.DownloadMethod != DeviceDownloadMethod.Sdk)
                {
                    continue;
                }

                devicesChecked++;
                var previousStatus = device.Status;

                try
                {
                    var client = _deviceClientFactory.GetClient(device);
                    var connected = await client.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);

                    if (connected)
                    {
                        device.MarkAsOnline();

                        var deviceInfo = await client.GetDeviceInfoAsync(cancellationToken);
                        if (deviceInfo != null)
                        {
                            // Actualizar la info de hardware del dispositivo si se recuperó
                            var hwInfo = new DeviceHardwareInfo(
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
                                deviceInfo.AttendanceRecordCapacity
                            );
                            device.UpdateDeviceInfo(hwInfo);

                            // Evaluar capacidad de memoria
                            if (deviceInfo.AttendanceRecordCapacity > 0)
                            {
                                double capacityUsed = (double)deviceInfo.AttendanceRecordCount / deviceInfo.AttendanceRecordCapacity;
                                if (capacityUsed >= 0.90 && config != null && !string.IsNullOrWhiteSpace(config.SystemFailureAlertEmails))
                                {
                                    var subject = $"⚠️ Alerta de Memoria: Dispositivo {device.Name}";
                                    var body = $"<p>El dispositivo <b>{device.Name}</b> (IP: {device.IpAddress}) ha alcanzado el {(capacityUsed * 100):F1}% de su capacidad de registros de asistencia.</p>" +
                                               $"<ul><li>Registros actuales: {deviceInfo.AttendanceRecordCount}</li>" +
                                               $"<li>Capacidad máxima: {deviceInfo.AttendanceRecordCapacity}</li></ul>" +
                                               $"<p>Por favor descargue los registros y limpie la memoria del dispositivo para evitar pérdida de datos.</p>";
                                    
                                    await _emailService.SendAlertAsync(subject, body, AlertLevel.SystemFailure, cancellationToken);
                                }
                            }
                        }

                        await client.DisconnectAsync(cancellationToken);
                    }
                    else
                    {
                        device.MarkAsOffline();
                        
                        // Notificar solo si el dispositivo acaba de desconectarse para no spamear
                        if (previousStatus != DeviceStatus.Offline && config != null && !string.IsNullOrWhiteSpace(config.SystemFailureAlertEmails))
                        {
                            var subject = $"🔴 Alerta de Conexión: Dispositivo {device.Name} Offline";
                            var body = $"<p>El dispositivo <b>{device.Name}</b> (IP: {device.IpAddress}) no respondió al monitoreo (Ping) y ha sido marcado como desconectado.</p>" +
                                       $"<p>Por favor revise la conectividad de red o la alimentación del equipo.</p>";
                            
                            await _emailService.SendAlertAsync(subject, body, AlertLevel.SystemFailure, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al monitorear dispositivo {DeviceName} ({DeviceIp})", device.Name, device.IpAddress);
                    device.MarkAsOffline();
                    
                    if (previousStatus != DeviceStatus.Offline && config != null && !string.IsNullOrWhiteSpace(config.SystemFailureAlertEmails))
                    {
                        await _emailService.SendAlertAsync(
                            $"🔴 Alerta de Conexión: Dispositivo {device.Name} Offline", 
                            $"<p>El dispositivo <b>{device.Name}</b> (IP: {device.IpAddress}) no respondió al monitoreo y ha sido marcado como desconectado.</p><p>Error: {ex.Message}</p>", 
                            AlertLevel.SystemFailure, 
                            cancellationToken);
                    }
                }

                await _deviceRepository.UpdateAsync(device, cancellationToken);
            }

            return Result<int>.Success(devicesChecked);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico durante el monitoreo de dispositivos");
            return Result<int>.Failure($"Error al monitorear dispositivos: {ex.Message}");
        }
    }
}
