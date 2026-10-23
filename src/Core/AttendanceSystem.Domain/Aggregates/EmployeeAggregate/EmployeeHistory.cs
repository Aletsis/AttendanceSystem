using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.EmployeeAggregate;

/// <summary>Período de relación laboral [inicio, fin]; sin fin = vigente (F06-08: alta, baja y reingreso).</summary>
public sealed class EmploymentPeriod : Entity<Guid>
{
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }

    private EmploymentPeriod() { }

    internal EmploymentPeriod(DateTime startDate, DateTime? endDate = null)
    {
        Id = Guid.NewGuid();
        StartDate = startDate.Date;
        EndDate = endDate?.Date;
    }

    public bool Contains(DateTime date) => date.Date >= StartDate && (!EndDate.HasValue || date.Date <= EndDate.Value);

    public bool Overlaps(DateTime start, DateTime end) => StartDate <= end.Date && (!EndDate.HasValue || EndDate.Value >= start.Date);

    internal void Close(DateTime endDate) => EndDate = endDate.Date < StartDate ? StartDate : endDate.Date;

    internal void MoveStart(DateTime startDate) => StartDate = startDate.Date;
}

/// <summary>
/// Asignación laboral vigente desde una fecha (F06-08): horario, tipo de turno, sucursal y días de descanso. El
/// cálculo de una fecha usa la asignación con la mayor <see cref="EffectiveFrom"/> que no la supere.
/// </summary>
public sealed class ScheduleAssignment : Entity<Guid>
{
    public DateTime EffectiveFrom { get; private set; }
    public ShiftId? ScheduleId { get; private set; }
    public ShiftType? ShiftType { get; private set; }
    public BranchId BranchId { get; private set; } = null!;
    /// <summary>Días de descanso vigentes; null en vigencias anteriores a que se fecharan (rigen los actuales).</summary>
    public List<WeekDay>? RestDays { get; private set; }

    private ScheduleAssignment() { }

    internal ScheduleAssignment(DateTime effectiveFrom, ShiftId? scheduleId, ShiftType? shiftType, BranchId branchId, IEnumerable<WeekDay> restDays)
    {
        Id = Guid.NewGuid();
        EffectiveFrom = effectiveFrom.Date;
        ScheduleId = scheduleId;
        ShiftType = shiftType;
        BranchId = branchId;
        RestDays = Normalize(restDays);
    }

    internal bool SameAs(ShiftId? scheduleId, ShiftType? shiftType, BranchId branchId, IEnumerable<WeekDay> restDays) =>
        ScheduleId == scheduleId && ShiftType == shiftType && BranchId == branchId &&
        RestDays != null && RestDays.SequenceEqual(Normalize(restDays));

    internal void Replace(ShiftId? scheduleId, ShiftType? shiftType, BranchId branchId, IEnumerable<WeekDay> restDays)
    {
        ScheduleId = scheduleId;
        ShiftType = shiftType;
        BranchId = branchId;
        RestDays = Normalize(restDays);
    }

    private static List<WeekDay> Normalize(IEnumerable<WeekDay> restDays) => restDays.Distinct().OrderBy(d => (int)d).ToList();
}
