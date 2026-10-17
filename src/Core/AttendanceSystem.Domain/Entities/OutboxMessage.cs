using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

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

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        Error = null;
    }

    public void MarkAsFailed(string error)
    {
        Error = error;
        RetryCount++;
    }
}
