using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;

public sealed class SystemAlert : AggregateRoot<SystemAlertId>
{
    public AlertType Type { get; private set; }
    public string? ReferenceId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string Message { get; private set; } = null!;
    public bool IsResolved { get; private set; }

    private SystemAlert() { }

    public static SystemAlert Create(AlertType type, string? referenceId, string message)
    {
        return new SystemAlert
        {
            Id = SystemAlertId.CreateNew(),
            Type = type,
            ReferenceId = referenceId,
            Timestamp = DateTime.UtcNow,
            Message = message,
            IsResolved = false
        };
    }

    public void Resolve()
    {
        IsResolved = true;
    }

    /// <summary>Actualiza el texto de una alerta vigente cuyo detalle cambió (p. ej. la fecha en que termina un roster).</summary>
    public void UpdateMessage(string message)
    {
        Message = message;
    }
}
