namespace AttendanceSystem.Domain.Services;

using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

/// <summary>
/// Violación de una invariante de pertenencia de marcajes (F02-02).
/// Se lanza antes de persistir para que la unidad de recálculo se revierta completa.
/// </summary>
public sealed class PunchAssignmentInvariantException : DomainException
{
    public IReadOnlyList<string> Violations { get; }

    public PunchAssignmentInvariantException(IReadOnlyList<string> violations)
        : base("Asignación de marcajes inválida: " + string.Join(" | ", violations))
    {
        Violations = violations;
    }
}

/// <summary>
/// Reglas comunes de pertenencia y selección de marcajes (F02-02 / F02-05).
/// </summary>
public static class PunchAssignmentPolicy
{
    /// <summary>
    /// Tope de compatibilidad temporal: una jornada sólo consume marcajes de su fecha laboral
    /// y de la siguiente (C-03, jornada máxima de 24 horas). Las asignaciones manuales, y la entrada
    /// anticipada automática de una jornada que empieza cerca de medianoche, pueden tomar el día anterior.
    /// </summary>
    public const int MaxDaysAfterWorkDate = 1;
    public const int MaxManualDaysBeforeWorkDate = 1;

    /// <summary>
    /// Orden estable y total de marcajes: por instante UTC y, en empate, por identificador.
    /// </summary>
    public static IOrderedEnumerable<AttendanceRecord> Chronological(IEnumerable<AttendanceRecord> records) =>
        records.OrderBy(r => r.CheckTime).ThenBy(r => r.Id.Value);

    /// <summary>
    /// Comparador estable para desempatar candidatos con la misma distancia al horario.
    /// </summary>
    public static int CompareChronologically(AttendanceRecord a, AttendanceRecord b)
    {
        var byTime = a.CheckTime.CompareTo(b.CheckTime);
        return byTime != 0 ? byTime : a.Id.Value.CompareTo(b.Id.Value);
    }

    /// <summary>
    /// Valida un conjunto de asignaciones propuestas para una jornada.
    /// </summary>
    /// <param name="employeeId">Empleado propietario de la jornada.</param>
    /// <param name="workDate">Fecha laboral de la jornada.</param>
    /// <param name="proposed">Asignaciones nuevas de la jornada.</param>
    /// <param name="recordsById">Marcajes conocidos (para validar propietario y fecha).</param>
    /// <param name="claimedElsewhere">Marcajes ya ocupados por otra jornada, con su fecha laboral.</param>
    public static IReadOnlyList<string> Validate(
        EmployeeId employeeId,
        DateOnly workDate,
        IReadOnlyCollection<PunchAssignment> proposed,
        IReadOnlyDictionary<AttendanceRecordId, AttendanceRecord> recordsById,
        IReadOnlyDictionary<AttendanceRecordId, DateOnly> claimedElsewhere)
    {
        var violations = new List<string>();

        foreach (var duplicate in proposed.GroupBy(a => a.AttendanceRecordId).Where(g => g.Count() > 1))
        {
            violations.Add($"El marcaje {duplicate.Key} ocupa {duplicate.Count()} posiciones en la jornada {employeeId.Value} {workDate:yyyy-MM-dd}.");
        }

        foreach (var slot in proposed.Where(a => a.IsPosition).GroupBy(a => a.Role).Where(g => g.Count() > 1))
        {
            violations.Add($"La posición {slot.Key} tiene {slot.Count()} marcajes en la jornada {employeeId.Value} {workDate:yyyy-MM-dd}.");
        }

        foreach (var assignment in proposed)
        {
            if (assignment.EmployeeId != employeeId || assignment.WorkDate != workDate)
            {
                violations.Add($"La asignación del marcaje {assignment.AttendanceRecordId} no corresponde a la jornada {employeeId.Value} {workDate:yyyy-MM-dd}.");
            }

            if (!recordsById.TryGetValue(assignment.AttendanceRecordId, out var record))
            {
                violations.Add($"El marcaje {assignment.AttendanceRecordId} asignado a {employeeId.Value} {workDate:yyyy-MM-dd} no existe o no fue cargado.");
                continue;
            }

            if (record.EmployeeId != employeeId)
            {
                violations.Add($"El marcaje {record.Id} pertenece a {record.EmployeeId.Value} y no puede asignarse a {employeeId.Value}.");
            }

            var recordWorkDate = record.GetWorkDate(record.TimeZoneId);
            // Manual: puede tomar una entrada del día anterior. Automático: sólo la entrada anticipada (o su doble
            // toque) de una jornada que empieza cerca de medianoche (cierre F06-01).
            var earliest = assignment.Origin == PunchAssignmentOrigin.Manual ||
                           assignment.Role is PunchAssignmentRole.CheckIn or PunchAssignmentRole.DiscardedDoubleTap
                ? workDate.AddDays(-MaxManualDaysBeforeWorkDate)
                : workDate;
            var latest = workDate.AddDays(MaxDaysAfterWorkDate);
            if (recordWorkDate < earliest || recordWorkDate > latest)
            {
                violations.Add($"El marcaje {record.Id} ({recordWorkDate:yyyy-MM-dd}) es incompatible con la jornada {workDate:yyyy-MM-dd}.");
            }

            if (claimedElsewhere.TryGetValue(assignment.AttendanceRecordId, out var otherWorkDate))
            {
                violations.Add($"El marcaje {record.Id} ya pertenece a la jornada {otherWorkDate:yyyy-MM-dd} y no puede reutilizarse en {workDate:yyyy-MM-dd}.");
            }
        }

        return violations;
    }

    public static void EnsureValid(
        EmployeeId employeeId,
        DateOnly workDate,
        IReadOnlyCollection<PunchAssignment> proposed,
        IReadOnlyDictionary<AttendanceRecordId, AttendanceRecord> recordsById,
        IReadOnlyDictionary<AttendanceRecordId, DateOnly> claimedElsewhere)
    {
        var violations = Validate(employeeId, workDate, proposed, recordsById, claimedElsewhere);
        if (violations.Count > 0)
        {
            throw new PunchAssignmentInvariantException(violations);
        }
    }

    /// <summary>
    /// Tipo inferido que corresponde a una posición principal.
    /// </summary>
    public static CheckType? InferredCheckType(PunchAssignmentRole role) => role switch
    {
        PunchAssignmentRole.CheckIn or PunchAssignmentRole.Block2CheckIn => CheckType.CheckIn,
        PunchAssignmentRole.CheckOut or PunchAssignmentRole.Block1CheckOut => CheckType.CheckOut,
        _ => null
    };
}
