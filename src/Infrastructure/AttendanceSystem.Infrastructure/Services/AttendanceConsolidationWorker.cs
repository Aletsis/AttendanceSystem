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

        foreach (var claim in claims)
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<AttendanceConsolidationProcessor>();
            await processor.ProcessAsync(claim, _consumerId, _options, cancellationToken);
        }

        return claims.Count;
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
