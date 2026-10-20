namespace AttendanceSystem.Application.Abstractions;

public sealed record OutboxHealth(
    int Pending,
    int InFlight,
    int DeadLettered,
    DateTime? OldestPendingOccurredOnUtc,
    IReadOnlyList<OutboxFailure> RecentDeadLetters);

public sealed record OutboxFailure(Guid MessageId, string Type, DateTime OccurredOnUtc, int Attempts, string? Error);

/// <summary>Operación del outbox (F04-08): estado, reintento de mensajes muertos y retención.</summary>
public interface IOutboxMonitor
{
    Task<OutboxHealth> GetHealthAsync(int recentDeadLetters = 20, CancellationToken cancellationToken = default);

    /// <summary>Reactiva mensajes muertos (todos o uno) tras corregir su causa.</summary>
    Task<int> ReviveDeadLettersAsync(Guid? messageId = null, CancellationToken cancellationToken = default);

    /// <summary>Elimina mensajes procesados más antiguos que la retención; conserva pendientes y muertos.</summary>
    Task<int> PurgeProcessedAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}
