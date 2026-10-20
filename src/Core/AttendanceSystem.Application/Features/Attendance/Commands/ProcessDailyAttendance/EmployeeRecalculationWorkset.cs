using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

/// <summary>
/// Datos y estado de pertenencia de un empleado durante una unidad de recálculo (F02-03 / F02-04).
/// Distingue las reclamaciones persistidas (filas de asignación, columnas heredadas de
/// DailyAttendance y ajustes manuales) de las producidas en esta ejecución, de modo que sólo se
/// liberan las de jornadas incluidas en la unidad.
/// </summary>
internal sealed class EmployeeRecalculationWorkset
{
    public EmployeeRecalculationWorkset(Employee employee, string? timeZoneId)
    {
        Employee = employee;
        TimeZoneId = timeZoneId;
    }

    public Employee Employee { get; }
    public string? TimeZoneId { get; }

    public Dictionary<DateTime, DailyAttendance> ExistingDaily { get; } = new();
    public Dictionary<DateTime, ShiftRoster> Rosters { get; } = new();
    public Dictionary<DateTime, AttendanceAdjustment> Adjustments { get; } = new();

    /// <summary>Marcajes propios del empleado cargados (candidatos potenciales).</summary>
    public Dictionary<AttendanceRecordId, AttendanceRecord> Records { get; } = new();

    /// <summary>Marcajes conocidos para validar (incluye los referenciados por ajustes aunque sean ajenos).</summary>
    public Dictionary<AttendanceRecordId, AttendanceRecord> KnownRecords { get; } = new();

    /// <summary>Filas de asignación rastreadas (las cargadas y las que esta ejecución añade o actualiza).</summary>
    public Dictionary<AttendanceRecordId, PunchAssignment> StoredAssignments { get; } = new();

    /// <summary>Estado de las filas tal como se leyeron, inmune a las actualizaciones en sitio de esta ejecución.</summary>
    private readonly Dictionary<AttendanceRecordId, (DateOnly Owner, bool IsManual)> _loadedClaims = new();

    /// <summary>Última fecha de jornada cuyos datos (roster, ajuste, existente y marcajes vecinos) están cargados.</summary>
    public DateTime LoadedThrough { get; set; }

    /// <summary>Jornadas que se reconstruyen en esta unidad.</summary>
    public SortedSet<DateTime> UnitDates { get; } = new();

    /// <summary>Asignaciones producidas en esta ejecución, por marcaje.</summary>
    public Dictionary<AttendanceRecordId, PunchAssignment> NewAssignments { get; } = new();

    public DateOnly WorkDateOf(AttendanceRecord record) => record.GetWorkDate(TimeZoneId);

    public void AddRecord(AttendanceRecord record)
    {
        if (record.EmployeeId == Employee.Id)
        {
            Records.TryAdd(record.Id, record);
        }
        KnownRecords.TryAdd(record.Id, record);
    }

    public bool IsInUnit(DateOnly workDate) => UnitDates.Contains(workDate.ToDateTime(TimeOnly.MinValue));

    public void TrackLoaded(PunchAssignment assignment)
    {
        if (StoredAssignments.TryAdd(assignment.AttendanceRecordId, assignment))
        {
            _loadedClaims.TryAdd(assignment.AttendanceRecordId, (assignment.WorkDate, assignment.Origin == PunchAssignmentOrigin.Manual));
        }
    }

    /// <summary>
    /// Reclamaciones previas a esta ejecución: marcaje → jornada propietaria y si es decisión manual.
    /// Precedencia: fila explícita, luego ajuste manual y por último columnas heredadas de DailyAttendance
    /// (datos anteriores a la tabla de asignaciones).
    /// </summary>
    public Dictionary<AttendanceRecordId, (DateOnly Owner, bool IsManual)> PersistedClaims()
    {
        var claims = new Dictionary<AttendanceRecordId, (DateOnly Owner, bool IsManual)>();

        foreach (var (recordId, claim) in _loadedClaims)
        {
            // Una fila es decisión manual sólo si el ajuste vigente de su jornada todavía decide ese marcaje:
            // la fila puede ser anterior a que RH retirara la decisión.
            var stillManual = claim.IsManual &&
                Adjustments.TryGetValue(claim.Owner.ToDateTime(TimeOnly.MinValue), out var ownerAdjustment) &&
                ManualRecordIds(ownerAdjustment).Contains(recordId);
            claims[recordId] = (claim.Owner, stillManual);
        }

        foreach (var (date, adjustment) in Adjustments.OrderBy(kv => kv.Key))
        {
            foreach (var recordId in ManualRecordIds(adjustment))
            {
                claims.TryAdd(recordId, (DateOnly.FromDateTime(date), true));
            }
        }

        foreach (var (date, daily) in ExistingDaily.OrderBy(kv => kv.Key))
        {
            foreach (var recordId in PositionRecordIds(daily))
            {
                claims.TryAdd(recordId, (DateOnly.FromDateTime(date), false));
            }
        }

        return claims;
    }

    /// <summary>
    /// Marcajes que no pueden asignarse automáticamente a <paramref name="workDate"/> (F02-04 / F02-05):
    /// <list type="bullet">
    /// <item>los de jornadas externas anteriores (precedencia cronológica: la más temprana conserva el marcaje);</item>
    /// <item>las decisiones manuales de cualquier jornada (C-05), salvo las propias si <paramref name="includeOwnManual"/> es falso;</item>
    /// <item>los ya tomados por otras jornadas de esta unidad.</item>
    /// </list>
    /// Las reclamaciones automáticas de jornadas externas posteriores no bloquean: si la jornada toma uno
    /// de esos marcajes, la extensión hacia adelante reconstruye la posterior.
    /// </summary>
    public Dictionary<AttendanceRecordId, DateOnly> Unavailable(DateOnly workDate, bool includeOwnManual)
    {
        var result = new Dictionary<AttendanceRecordId, DateOnly>();

        foreach (var (recordId, (owner, isManual)) in PersistedClaims())
        {
            // Las reclamaciones previas de la propia jornada siempre se liberan al recalcularla.
            if (owner == workDate || IsInUnit(owner)) continue;
            if (owner < workDate || isManual) result[recordId] = owner;
        }

        foreach (var (date, adjustment) in Adjustments)
        {
            var owner = DateOnly.FromDateTime(date);
            if (owner == workDate && !includeOwnManual) continue;
            foreach (var recordId in ManualRecordIds(adjustment))
            {
                result.TryAdd(recordId, owner);
            }
        }

        foreach (var assignment in NewAssignments.Values)
        {
            if (assignment.WorkDate != workDate) result[assignment.AttendanceRecordId] = assignment.WorkDate;
        }

        return result;
    }

    /// <summary>
    /// Reclamaciones de la unidad sobre marcajes de <paramref name="recordWorkDate"/>, antes y después de reconstruir.
    /// Si difieren, la jornada siguiente depende del resultado y debe incluirse en la unidad.
    /// </summary>
    public (HashSet<AttendanceRecordId> Before, HashSet<AttendanceRecordId> After) BoundaryClaims(DateOnly recordWorkDate)
    {
        var boundary = Records.Values
            .Where(r => WorkDateOf(r) == recordWorkDate)
            .Select(r => r.Id)
            .ToHashSet();

        var before = PersistedClaims()
            .Where(kv => boundary.Contains(kv.Key) && IsInUnit(kv.Value.Owner))
            .Select(kv => kv.Key)
            .ToHashSet();

        var after = NewAssignments.Keys.Where(boundary.Contains).ToHashSet();

        return (before, after);
    }

    public bool HasPersistedJornada(DateTime date) =>
        ExistingDaily.ContainsKey(date.Date) ||
        _loadedClaims.Values.Any(c => c.Owner == DateOnly.FromDateTime(date));

    /// <summary>Marcajes con decisión manual (asignados o excluidos con vacío forzado).</summary>
    public static IEnumerable<AttendanceRecordId> ManualRecordIds(AttendanceAdjustment adjustment) =>
        adjustment.DecidedRecordIds();

    public static IEnumerable<AttendanceRecordId> PositionRecordIds(DailyAttendance daily)
    {
        if (daily.CheckInRecordId != null) yield return daily.CheckInRecordId;
        if (daily.Block1CheckOutRecordId != null) yield return daily.Block1CheckOutRecordId;
        if (daily.Block2CheckInRecordId != null) yield return daily.Block2CheckInRecordId;
        if (daily.CheckOutRecordId != null) yield return daily.CheckOutRecordId;
    }
}
