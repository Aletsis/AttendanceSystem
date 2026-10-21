using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Consolidation;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Consumidor durable de solicitudes de consolidación (F04-05). Lee el trabajo de la base de datos:
/// un aviso perdido, un reinicio o la caída de Hangfire no pierden solicitudes, y un lease vencido
/// (proceso caído a mitad del cálculo) devuelve la solicitud a otro consumidor. Varias instancias pueden
/// ejecutarse a la vez: la reclamación usa <c>SKIP LOCKED</c> y el recálculo, el bloqueo por empleado.
/// Cada solicitud se procesa en su propio ámbito (contexto limpio).
/// </summary>
public sealed class AttendanceConsolidationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConsolidationWakeupSignal _wakeup;
    private readonly ConsolidationOptions _options;
    private readonly ILogger<AttendanceConsolidationWorker> _logger;
    private readonly string _consumerId = $"consolidation:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private DateTime _nextPurgeUtc = DateTime.MinValue;

    public AttendanceConsolidationWorker(
        IServiceScopeFactory scopeFactory,
        ConsolidationWakeupSignal wakeup,
        ConsolidationOptions options,
        ILogger<AttendanceConsolidationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _wakeup = wakeup;
        _options = options;
        _logger = logger;
    }

    public string ConsumerId => _consumerId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumidor de consolidación {ConsumerId} iniciado.", _consumerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;
            try
            {
                handled = await DrainOnceAsync(stoppingToken);
                await PurgeIfDueAsync(stoppingToken);
                await MonitorHealthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el consumidor de consolidación; se reintentará en el siguiente ciclo.");
            }

            if (handled > 0) continue;

            try
            {
                await _wakeup.WaitAsync(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Reclama y procesa un lote; devuelve cuántas solicitudes se atendieron.</summary>
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Aggregates.DailyAttendanceAggregate.ConsolidationClaim> claims;
        using (var scope = _scopeFactory.CreateScope())
        {
            var restoreState = scope.ServiceProvider.GetService<IRestoreStateService>();
            if (restoreState != null && !restoreState.CanWrite) return 0;

            var repository = scope.ServiceProvider.GetRequiredService<IAttendanceConsolidationRepository>();
            claims = await repository.ClaimAsync(
                _consumerId, _options.BatchSize, _options.Lease, _options.QuietPeriod, _options.MaxDelay, cancellationToken);
        }

        if (claims.Count > 0)
        {
            // F07-08: las solicitudes reclamadas se recalculan juntas (por rango) en un ámbito; el recálculo libera el
            // seguimiento de entidades tras cada lote interno.
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<AttendanceConsolidationProcessor>();
            await processor.ProcessBatchAsync(claims, _consumerId, _options, cancellationToken);
        }

        return claims.Count;
    }

    private DateTime _nextHealthUtc = DateTime.MinValue;
    private readonly HashSet<string> _activeAlerts = new();

    /// <summary>
    /// F07-01: actualiza las métricas de salud y emite alertas (log de advertencia, una vez por cambio de estado) por
    /// pendientes envejecidos, reintentos agotados y eventos en dead letter.
    /// </summary>
    public async Task MonitorHealthAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _nextHealthUtc) return;
        _nextHealthUtc = DateTime.UtcNow.Add(_options.HealthCheckInterval);

        // La supervisión nunca detiene el consumo: un fallo al leer la salud sólo se registra.
        ConsolidationHealth consolidation;
        OutboxHealth? outbox;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            consolidation = await scope.ServiceProvider.GetRequiredService<IAttendanceConsolidationRepository>().GetHealthAsync(5, cancellationToken);
            var monitor = scope.ServiceProvider.GetService<IOutboxMonitor>();
            outbox = monitor == null ? null : await monitor.GetHealthAsync(5, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo leer la salud de consolidación y outbox.");
            return;
        }

        var oldestAge = consolidation.OldestPendingRequestedAt is { } oldest ? Math.Max(0, (DateTime.UtcNow - oldest).TotalSeconds) : 0;
        AttendanceSystem.Application.Common.AttendanceTelemetry.LastHealth = new AttendanceSystem.Application.Common.OperationalHealth(
            consolidation.Pending, consolidation.Failed, oldestAge, outbox?.Pending ?? 0, outbox?.DeadLettered ?? 0);

        Alert("consolidation-age", oldestAge > _options.AlertPendingAge.TotalSeconds,
            "ALERTA: la solicitud de consolidación pendiente más antigua tiene {Age:F0} s (umbral {Threshold:F0} s); {Pending} pendientes.",
            oldestAge, _options.AlertPendingAge.TotalSeconds, consolidation.Pending);
        Alert("consolidation-failed", consolidation.Failed > 0,
            "ALERTA: {Failed} solicitudes de consolidación agotaron sus reintentos; reintentar desde api/consolidation tras corregir la causa.",
            consolidation.Failed);
        Alert("outbox-dead", (outbox?.DeadLettered ?? 0) > 0,
            "ALERTA: {Dead} eventos del outbox en dead letter; revisar y revivir desde api/consolidation.", outbox?.DeadLettered ?? 0);
    }

    private void Alert(string key, bool active, string message, params object[] args)
    {
        if (active && _activeAlerts.Add(key)) _logger.LogWarning(message, args);
        else if (!active && _activeAlerts.Remove(key)) _logger.LogInformation("Alerta {Alert} resuelta.", key);
    }

    private async Task PurgeIfDueAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _nextPurgeUtc) return;
        _nextPurgeUtc = DateTime.UtcNow.Add(_options.PurgeInterval);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAttendanceConsolidationRepository>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxMonitor>();

        // F04-08: se eliminan sólo terminados con éxito; fallidas y mensajes muertos se conservan como evidencia.
        var requests = await repository.PurgeFinishedAsync(_options.FinishedRequestRetention, cancellationToken);
        var messages = await outbox.PurgeProcessedAsync(_options.ProcessedOutboxRetention, cancellationToken);
        if (requests + messages > 0)
        {
            _logger.LogInformation("Retención: {Requests} solicitudes terminadas y {Messages} eventos procesados eliminados.", requests, messages);
        }
    }
}
