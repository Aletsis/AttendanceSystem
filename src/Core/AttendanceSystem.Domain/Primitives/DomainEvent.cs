using MediatR;

namespace AttendanceSystem.Domain.Primitives;

public abstract record DomainEvent(DateTime OccurredOn) : INotification
{
    public Guid EventId { get; } = Guid.NewGuid();
}