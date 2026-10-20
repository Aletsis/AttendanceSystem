using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Entities;

/// <summary>
/// Evento de dominio persistido en la misma transacción que su agregado (F04-01).
/// <code>
/// Pendiente ──reclamar (LockedBy, LockedUntilUtc)──► En publicación ──éxito──► Procesado
///    ▲                                                   │
///    └──── fallo: RetryCount+1, NextAttemptOnUtc ◄────────┤
///                                                        └── intentos agotados ──► Muerto (DeadLetteredOnUtc)
/// </code>
/// Entrega al menos una vez: un lease vencido (proceso caído) vuelve a dejarlo disponible, por lo
/// que los consumidores deben ser idempotentes. Un mensaje muerto queda visible y puede reintentarse.
/// </summary>
public class OutboxMessage
{
    public const int DefaultMaxAttempts = 5;

    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public DateTime? NextAttemptOnUtc { get; private set; }
    public DateTime? DeadLetteredOnUtc { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(DomainEvent domainEvent, string content)
    {
        return new OutboxMessage
        {
            Id = domainEvent.EventId != Guid.Empty ? domainEvent.EventId : Guid.NewGuid(),
            Type = domainEvent.GetType().AssemblyQualifiedName ?? domainEvent.GetType().FullName!,
            Content = content,
            OccurredOnUtc = domainEvent.OccurredOn.Kind == DateTimeKind.Utc
                ? domainEvent.OccurredOn
                : (domainEvent.OccurredOn == default ? DateTime.UtcNow : domainEvent.OccurredOn.ToUniversalTime()),
            ProcessedOnUtc = null,
            RetryCount = 0
        };
    }

    /// <summary>Reclama el mensaje para publicarlo; nadie más lo toma hasta que venza el lease.</summary>
    public void Lock(string lockedBy, DateTime lockedUntilUtc)
    {
        LockedBy = lockedBy;
        LockedUntilUtc = lockedUntilUtc;
    }

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        Error = null;
        Unlock();
    }

    /// <summary>
    /// Registra un intento fallido. Con backoff exponencial (base × 2^intentos, máx. 1 h) y, al agotar
    /// <paramref name="maxAttempts"/>, pasa a mensaje muerto.
    /// </summary>
    public void MarkAsFailed(string error, int maxAttempts = DefaultMaxAttempts, TimeSpan? baseBackoff = null)
    {
        Error = error;
        RetryCount++;
        Unlock();

        if (RetryCount >= maxAttempts)
        {
            DeadLetteredOnUtc = DateTime.UtcNow;
            NextAttemptOnUtc = null;
            return;
        }

        var delay = TimeSpan.FromTicks((baseBackoff ?? TimeSpan.FromSeconds(30)).Ticks * (1L << Math.Min(RetryCount - 1, 20)));
        if (delay > TimeSpan.FromHours(1)) delay = TimeSpan.FromHours(1);
        NextAttemptOnUtc = DateTime.UtcNow.Add(delay);
    }

    /// <summary>Libera el mensaje sin contar intento (cancelación o apagado).</summary>
    public void Release() => Unlock();

    /// <summary>Reintento operativo de un mensaje muerto tras corregir la causa.</summary>
    public void Revive()
    {
        DeadLetteredOnUtc = null;
        RetryCount = 0;
        NextAttemptOnUtc = null;
        Unlock();
    }

    private void Unlock()
    {
        LockedBy = null;
        LockedUntilUtc = null;
    }
}
