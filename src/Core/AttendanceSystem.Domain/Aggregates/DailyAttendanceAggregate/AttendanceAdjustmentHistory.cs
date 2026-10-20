namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Registro inmutable de una decisión manual (F05-06): quién, cuándo, por qué, qué cambió (antes/después)
/// y la revisión resultante del ajuste. No contiene datos personales más allá del identificador de empleado.
/// </summary>
public sealed class AttendanceAdjustmentHistory : Entity<Guid>
{
    public Guid AdjustmentId { get; private set; }
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime Date { get; private set; }
    public int Revision { get; private set; }
    public string Action { get; private set; } = null!;
    public string Before { get; private set; } = null!;
    public string After { get; private set; } = null!;
    public string? Reason { get; private set; }
    public string ChangedBy { get; private set; } = null!;
    public DateTime ChangedAt { get; private set; }

    private AttendanceAdjustmentHistory() { } // EF Core

    public static AttendanceAdjustmentHistory Record(
        AttendanceAdjustment adjustment,
        string action,
        string before,
        string? reason,
        string changedBy,
        DateTime? changedAt = null)
    {
        return new AttendanceAdjustmentHistory
        {
            Id = Guid.NewGuid(),
            AdjustmentId = adjustment.Id,
            EmployeeId = adjustment.EmployeeId,
            Date = adjustment.Date,
            Revision = adjustment.Revision,
            Action = action,
            Before = before,
            After = adjustment.Describe(),
            Reason = reason,
            ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "Sistema" : changedBy,
            ChangedAt = changedAt ?? DateTime.UtcNow
        };
    }
}
