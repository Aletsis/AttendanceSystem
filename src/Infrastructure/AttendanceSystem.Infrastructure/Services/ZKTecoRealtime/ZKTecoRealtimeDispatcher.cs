using System.Globalization;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordRealtimePunches;
using AttendanceSystem.ZKTeco.Grpc;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services.ZKTecoRealtime;

/// <summary>
/// Procesa lo que llega del stream de tiempo real del servicio ZKTeco: junta los marcajes por reloj y los guarda en
/// lotes, pide una descarga de respaldo cuando una escucha se (re)conecta o un lote no se pudo guardar, y mantiene viva
/// la actividad de los relojes conectados para la regla R4 de la cuarentena.
/// </summary>
public sealed class ZKTecoRealtimeDispatcher
{
    /// <summary>Entre dos descargas de respaldo del mismo reloj; evita una por cada reconexión de una red inestable.</summary>
    public static readonly TimeSpan CatchUpCooldown = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceHeartbeatTracker _heartbeats;
    private readonly ILogger<ZKTecoRealtimeDispatcher> _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<RealtimePunchDto>> _pending = new();
    private readonly HashSet<string> _connected = new();
    private readonly Dictionary<string, DateTimeOffset> _lastCatchUp = new();

    public ZKTecoRealtimeDispatcher(
        IServiceScopeFactory scopeFactory,
        IDeviceHeartbeatTracker heartbeats,
        ILogger<ZKTecoRealtimeDispatcher> logger,
        TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _heartbeats = heartbeats;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public void Handle(RealtimeUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.DeviceId)) return;

        switch (update.PayloadCase)
        {
            case RealtimeUpdate.PayloadOneofCase.Punch:
                if (!DateTime.TryParse(update.Punch.CheckTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkTime))
                {
                    _logger.LogWarning("Tiempo real: marcaje con hora ilegible '{CheckTime}' del reloj {DeviceId}.", update.Punch.CheckTime, update.DeviceId);
                    return;
                }
                var punch = new RealtimePunchDto(update.Punch.UserId, DateTime.SpecifyKind(checkTime, DateTimeKind.Unspecified),
                    update.Punch.VerifyMode, update.Punch.InOutMode);
                lock (_gate)
                {
                    if (!_pending.TryGetValue(update.DeviceId, out var list)) _pending[update.DeviceId] = list = new List<RealtimePunchDto>();
                    list.Add(punch);
                }
                break;

            case RealtimeUpdate.PayloadOneofCase.Connection:
                lock (_gate)
                {
                    if (update.Connection.Connected) _connected.Add(update.DeviceId);
                    else _connected.Remove(update.DeviceId);
                }
                if (update.Connection.Connected)
                {
                    _heartbeats.RecordActivity(string.Empty, update.DeviceId);
                    // Lo marcado mientras no se escuchaba (o antes de esta suscripción) sólo lo trae una descarga.
                    RequestCatchUp(update.DeviceId, $"escucha {update.Connection.Message}");
                }
                else
                {
                    _logger.LogWarning("Tiempo real: el reloj {DeviceId} quedó sin escucha ({Message}).", update.DeviceId, update.Connection.Message);
                }
                break;
        }
    }

    /// <summary>Guarda los marcajes pendientes, un lote por reloj. Si un lote falla, se pide la descarga de respaldo.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, List<RealtimePunchDto>>> batches;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            batches = _pending.ToList();
            _pending.Clear();
        }

        foreach (var (deviceId, punches) in batches)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(new RecordRealtimePunchesCommand(deviceId, punches), cancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogError("Tiempo real: no se guardaron {Count} marcajes del reloj {DeviceId}: {Error}", punches.Count, deviceId, result.Error);
                    RequestCatchUp(deviceId, "lote no guardado", force: true);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Tiempo real: error guardando {Count} marcajes del reloj {DeviceId}.", punches.Count, deviceId);
                RequestCatchUp(deviceId, "lote no guardado", force: true);
            }
        }
    }

    /// <summary>
    /// Registra actividad de los relojes con la escucha conectada. El registro considera interrumpida una comunicación
    /// tras 5 minutos sin actividad, y un reloj sin marcajes no envía nada mientras su escucha sigue viva.
    /// </summary>
    public void KeepAlive()
    {
        string[] connected;
        lock (_gate) connected = _connected.ToArray();
        foreach (var deviceId in connected) _heartbeats.RecordActivity(string.Empty, deviceId);
    }

    private void RequestCatchUp(string deviceId, string reason, bool force = false)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (!force && _lastCatchUp.TryGetValue(deviceId, out var last) && now - last < CatchUpCooldown) return;
            _lastCatchUp[deviceId] = now;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IAttendanceJobScheduler>().EnqueueDeviceCatchUp(deviceId);
            _logger.LogInformation("Tiempo real: descarga de respaldo del reloj {DeviceId} encolada ({Reason}).", deviceId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tiempo real: no se pudo encolar la descarga de respaldo del reloj {DeviceId}.", deviceId);
        }
    }
}
