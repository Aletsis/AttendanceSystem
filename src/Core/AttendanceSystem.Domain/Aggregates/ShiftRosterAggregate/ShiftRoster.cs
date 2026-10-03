using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;

public sealed class ShiftRoster : AggregateRoot<ShiftRosterId>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime Date { get; private set; }
    public ShiftId? ShiftId { get; private set; }
    public bool IsRestDay { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ShiftRoster() { }

    public static ShiftRoster Create(
        EmployeeId employeeId,
        DateTime date,
        ShiftId? shiftId,
        bool isRestDay,
        string? notes = null)
    {
        if (employeeId == null)
            throw new ArgumentNullException(nameof(employeeId));

        if (!isRestDay && shiftId == null)
            throw new DomainException("Un registro en el roster debe tener un turno asignado o estar marcado como día de descanso.");

        var roster = new ShiftRoster
        {
            Id = ShiftRosterId.CreateNew(),
            EmployeeId = employeeId,
            Date = date.Date,
            ShiftId = shiftId,
            IsRestDay = isRestDay,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        return roster;
    }

    public void Update(ShiftId? shiftId, bool isRestDay, string? notes = null)
    {
        if (!isRestDay && shiftId == null)
            throw new DomainException("Un registro en el roster debe tener un turno asignado o estar marcado como día de descanso.");

        ShiftId = shiftId;
        IsRestDay = isRestDay;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
