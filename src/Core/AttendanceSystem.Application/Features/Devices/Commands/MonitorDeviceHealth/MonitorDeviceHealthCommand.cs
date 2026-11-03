using System.Net;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth;

/// <summary>
/// Revisa los relojes activos: los ADMS por su último latido y los SDK conectándose a ellos. Guarda el estado y la
/// información de hardware, y avisa por correo una vez por cambio: al desconectarse, al volver y al llenarse la memoria.
/// Cada aviso abierto queda como alerta de sistema y, mientras siga abierto, no se repite. Devuelve los relojes revisados.
/// </summary>
[RequiresPermission(Permissions.DevicesManage)]
public sealed record MonitorDeviceHealthCommand() : IRequest<Result<int>>;

public sealed class MonitorDeviceHealthCommandHandler : IRequestHandler<MonitorDeviceHealthCommand, Result<int>>
{
    /// <summary>Un reloj ADMS sin latidos en este lapso se considera desconectado.</summary>
    public static readonly TimeSpan AdmsOnlineWindow = TimeSpan.FromSeconds(60);
    /// <summary>Uso de la memoria de registros a partir del cual se avisa.</summary>
    public const double MemoryAlertThreshold = 0.90;

    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IEmailService _emailService;
    private readonly IMediator _mediator;
    private readonly ILogger<MonitorDeviceHealthCommandHandler> _logger;
    private readonly IDeviceHeartbeatTracker _heartbeatTracker;
    private readonly ISystemAlertRepository _alertRepository;
    private readonly IDeviceLockService _deviceLock;
    private readonly IUnitOfWork _unitOfWork;

    public MonitorDeviceHealthCommandHandler(
        IDeviceRepository deviceRepository,
        IDeviceClientFactory deviceClientFactory,
        IEmailService emailService,
        IMediator mediator,
        ILogger<MonitorDeviceHealthCommandHandler> logger,
        IDeviceHeartbeatTracker heartbeatTracker,
        ISystemAlertRepository alertRepository,
        IDeviceLockService deviceLock,
        IUnitOfWork unitOfWork)
    {
        _deviceRepository = deviceRepository;
        _deviceClientFactory = deviceClientFactory;
        _emailService = emailService;
        _mediator = mediator;
        _logger = logger;
        _heartbeatTracker = heartbeatTracker;
        _alertRepository = alertRepository;
        _deviceLock = deviceLock;
        _unitOfWork = unitOfWork;
    }

    private sealed record Probe(bool Online, string? OfflineReason, DeviceInfoDto? Info);

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
            var openAlerts = (await _alertRepository.GetActiveAlertsAsync(cancellationToken))
                .Where(a => a.Type is AlertType.DeviceOffline or AlertType.DeviceMemoryFull)
                .ToList();
            var emails = new List<(string Subject, string Body)>();
            // Tras arrancar, el registro de latidos está vacío: hasta que los relojes vuelvan a reportarse no se sabe
            // cuáles están desconectados.
            var admsTrackingWarm = DateTime.UtcNow - _heartbeatTracker.TrackingSinceUtc >= AdmsOnlineWindow * 2;
            int devicesChecked = 0;

            foreach (var device in devices)
            {
                if (!device.IsActive)
                {
                    continue;
                }

                devicesChecked++;

                if (device.DownloadMethod == DeviceDownloadMethod.Adms)
                {
                    var isOnline = _heartbeatTracker.IsDeviceOnline(device.HardwareInfo?.SerialNumber, device.Id.Value, AdmsOnlineWindow);
                    if (!isOnline && !admsTrackingWarm) continue;

                    await TrackConnectivityAsync(device, isOnline, $"no ha reportado actividad en los últimos {AdmsOnlineWindow.TotalSeconds:F0} segundos", openAlerts, emails, cancellationToken);
                    continue;
                }

                // Una descarga o un ajuste de hora en curso tiene la conexión: el reloj responde y se revisa en la siguiente pasada.
                Probe? probe = null;
                var free = await _deviceLock.TryExecuteWithLockAsync(device.Id.Value, async () => probe = await ProbeAsync(device, cancellationToken), cancellationToken);
                if (!free || probe == null)
                {
                    _logger.LogInformation("El dispositivo {DeviceName} está en uso por otra operación; se revisará en la siguiente pasada.", device.Name);
                    continue;
                }

                await TrackConnectivityAsync(device, probe.Online, probe.OfflineReason ?? "", openAlerts, emails, cancellationToken);
                if (probe.Info is { } info)
                {
                    device.UpdateDeviceInfo(new DeviceHardwareInfo(
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
                        info.AttendanceRecordCapacity,
                        info.PushVersion,
                        info.SdkVersion,
                        info.MacAddress));
                    await TrackMemoryAsync(device, info, openAlerts, emails, cancellationToken);
                }
            }

            // Antes no se guardaba: el estado leído siempre era el anterior y el aviso de desconexión se repetía en cada pasada.
            // Los relojes vienen con seguimiento: sólo se escriben las columnas que cambiaron (Update las marcaría todas y
            // pisaría lo que una descarga o ADMS guardaron mientras se revisaban los relojes).
            // Se guarda antes de enviar: un correo fallido no hace que el aviso se repita.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(config.SystemFailureAlertEmails))
            {
                foreach (var (subject, body) in emails)
                {
                    try
                    {
                        await _emailService.SendAlertAsync(subject, body, AlertLevel.SystemFailure, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "No se pudo enviar el aviso de dispositivos: {Subject}", subject);
                    }
                }
            }

            return Result<int>.Success(devicesChecked);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error crítico durante el monitoreo de dispositivos");
            return Result<int>.Failure($"Error al monitorear dispositivos: {ex.Message}");
        }
    }

    private async Task<Probe> ProbeAsync(Device device, CancellationToken cancellationToken)
    {
        try
        {
            var client = _deviceClientFactory.GetClient(device);
            if (!await client.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken))
            {
                return new Probe(false, "no respondió al monitoreo (Ping)", null);
            }

            try
            {
                return new Probe(true, null, await client.GetDeviceInfoAsync(cancellationToken));
            }
            finally
            {
                await client.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error al monitorear dispositivo {DeviceName} ({DeviceIp})", device.Name, device.IpAddress);
            return new Probe(false, $"no respondió al monitoreo (error: {ex.Message})", null);
        }
    }

    private async Task TrackConnectivityAsync(
        Device device, bool online, string offlineReason, List<SystemAlert> openAlerts, List<(string, string)> emails, CancellationToken cancellationToken)
    {
        if (online) device.MarkAsOnline();
        else device.MarkAsOffline();

        var open = openAlerts.FirstOrDefault(a => a.Type == AlertType.DeviceOffline && a.ReferenceId == device.Id.Value);
        if (!online && open == null)
        {
            var message = $"El dispositivo {device.Name} ({Location(device)}) {offlineReason} y se marcó como desconectado.";
            var alert = SystemAlert.Create(AlertType.DeviceOffline, device.Id.Value, message);
            await _alertRepository.AddAsync(alert, cancellationToken);
            openAlerts.Add(alert);
            emails.Add((
                $"🔴 Alerta de Conexión: Dispositivo {device.Name} Offline",
                $"<p>{WebUtility.HtmlEncode(message)}</p><p>Por favor revise la conectividad de red o la alimentación del equipo.</p>"));
        }
        else if (online && open != null)
        {
            open.Resolve();
            emails.Add((
                $"🟢 Dispositivo {device.Name} en línea de nuevo",
                $"<p>El dispositivo <b>{WebUtility.HtmlEncode(device.Name)}</b> ({WebUtility.HtmlEncode(Location(device))}) volvió a comunicarse. " +
                $"Estaba desconectado desde el {open.Timestamp.ToLocalTime():dd/MM/yyyy HH:mm}.</p>"));
        }
    }

    private async Task TrackMemoryAsync(
        Device device, DeviceInfoDto info, List<SystemAlert> openAlerts, List<(string, string)> emails, CancellationToken cancellationToken)
    {
        if (info.AttendanceRecordCapacity <= 0) return;

        var used = (double)info.AttendanceRecordCount / info.AttendanceRecordCapacity;
        var open = openAlerts.FirstOrDefault(a => a.Type == AlertType.DeviceMemoryFull && a.ReferenceId == device.Id.Value);
        if (used >= MemoryAlertThreshold && open == null)
        {
            var alert = SystemAlert.Create(AlertType.DeviceMemoryFull, device.Id.Value,
                $"El dispositivo {device.Name} ({Location(device)}) tiene ocupado el {used * 100:F1}% de su memoria de registros.");
            await _alertRepository.AddAsync(alert, cancellationToken);
            openAlerts.Add(alert);
            emails.Add((
                $"⚠️ Alerta de Memoria: Dispositivo {device.Name}",
                $"<p>El dispositivo <b>{WebUtility.HtmlEncode(device.Name)}</b> ({WebUtility.HtmlEncode(Location(device))}) ha alcanzado el {used * 100:F1}% de su capacidad de registros de asistencia.</p>" +
                $"<ul><li>Registros actuales: {info.AttendanceRecordCount}</li>" +
                $"<li>Capacidad máxima: {info.AttendanceRecordCapacity}</li></ul>" +
                "<p>Por favor descargue los registros y limpie la memoria del dispositivo para evitar pérdida de datos.</p>"));
        }
        else if (used < MemoryAlertThreshold && open != null)
        {
            // Se limpió la memoria: el siguiente llenado vuelve a avisar.
            open.Resolve();
        }
    }

    private static string Location(Device device) =>
        device.DownloadMethod == DeviceDownloadMethod.Adms ? $"SN: {device.HardwareInfo?.SerialNumber}" : $"IP: {device.IpAddress}";
}
