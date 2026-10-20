using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Outbox;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Publica los mensajes del outbox que la ruta inmediata no pudo publicar (fallo, caída antes o
/// después de guardar, transacción confirmada fuera del protocolo). Reclama con lease y
/// <c>SKIP LOCKED</c>, por lo que varias instancias del servidor pueden ejecutarse a la vez (F04-02).
/// </summary>
public class OutboxBackgroundProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxBackgroundProcessor> _logger;
    private readonly string _consumerId = $"outbox:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public OutboxBackgroundProcessor(
        IServiceProvider serviceProvider,
        ILogger<OutboxBackgroundProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processed = 0;
            try
            {
                processed = await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el procesamiento en segundo plano del Outbox.");
            }

            if (processed > 0) continue;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var restoreState = scope.ServiceProvider.GetService<IRestoreStateService>();
        if (restoreState != null && !restoreState.CanWrite)
        {
            return 0;
        }

        var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var store = new OutboxStore(context);

        var claimed = await store.ClaimAsync(_consumerId, 50, OutboxStore.DefaultLease, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        await OutboxStore.PublishClaimedAsync(claimed.Select(m => (m, (INotification?)null)).ToList(), publisher, cancellationToken);
        await context.SaveChangesAsync(CancellationToken.None);

        var processed = claimed.Count(m => m.ProcessedOnUtc != null);
        var dead = claimed.Count(m => m.DeadLetteredOnUtc != null);
        if (dead > 0)
        {
            _logger.LogError("Outbox: {Dead} mensajes agotaron sus intentos y quedaron como mensajes muertos.", dead);
        }

        return processed;
    }
}
