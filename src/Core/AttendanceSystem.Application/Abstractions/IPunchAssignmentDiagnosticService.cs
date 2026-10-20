namespace AttendanceSystem.Application.Abstractions;

public enum PunchAssignmentIssueKind
{
    /// <summary>El marcaje aparece en más de una posición o jornada.</summary>
    MultipleReferences = 1,

    /// <summary>La jornada de un empleado referencia un marcaje de otro.</summary>
    ForeignEmployee = 2,

    /// <summary>La jornada referencia un marcaje inexistente.</summary>
    MissingRecord = 3,

    /// <summary>El marcaje está referenciado pero su estado es Pending (o similar incompatible).</summary>
    IncompatibleStatus = 4,

    /// <summary>La fecha laboral del marcaje queda fuera de la ventana de la jornada.</summary>
    TemporalIncompatibility = 5,

    /// <summary>Ya existe una fila de asignación que apunta a otra jornada.</summary>
    ConflictingAssignmentRow = 6
}

public sealed record JornadaKey(string EmployeeId, DateOnly WorkDate);

public sealed record PunchAssignmentIssue(
    PunchAssignmentIssueKind Kind,
    string RecordId,
    string EmployeeId,
    IReadOnlyList<DateOnly> WorkDates,
    bool InvolvesManualDecision,
    string Detail);

public sealed record PunchAssignmentDiagnosticReport(
    int DailyAttendancesInspected,
    int ReferencesInspected,
    int AlreadyMaterializedCount,
    int ProposedBackfillCount,
    int ProcessedWithoutAssignmentCount,
    IReadOnlyList<PunchAssignmentIssue> Issues,
    IReadOnlyList<JornadaKey> JornadasToRecalculate);

public sealed record PunchAssignmentRepairResult(
    int CreatedCount,
    int AlreadyMaterializedCount,
    int SkippedForReviewCount,
    bool IsDryRun);

/// <summary>
/// F02-07: diagnóstico y reparación revisable de asignaciones heredadas (columnas de DailyAttendance
/// anteriores a la tabla PunchAssignments). La reparación sólo materializa referencias inequívocas;
/// los conflictos se listan para revisión y recálculo, conservando las decisiones manuales.
/// </summary>
public interface IPunchAssignmentDiagnosticService
{
    Task<PunchAssignmentDiagnosticReport> DiagnoseAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    Task<PunchAssignmentRepairResult> RepairAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool dryRun = true,
        CancellationToken cancellationToken = default);
}
