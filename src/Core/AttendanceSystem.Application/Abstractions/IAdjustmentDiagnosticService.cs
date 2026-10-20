namespace AttendanceSystem.Application.Abstractions;

public enum AdjustmentFindingKind
{
    /// <summary>Decisión explícita y válida: se conserva con certeza.</summary>
    Preservable = 1,

    /// <summary>La jornada indica ajustes manuales pero no hay decisión guardada: no se puede reconstruir.</summary>
    LostDecision = 2,

    /// <summary>Ajuste sin ninguna decisión vigente: probable desasignación antigua sin rastro de vacío forzado.</summary>
    NoEvidenceOfIntent = 3,

    /// <summary>Ajuste creado automáticamente a partir de un resultado calculado; requiere confirmación de RH.</summary>
    DerivedFromCalculatedResult = 4,

    /// <summary>La decisión referencia un marcaje inexistente, ajeno o fuera de la ventana de la jornada.</summary>
    InvalidPunchReference = 5,

    /// <summary>La decisión referencia un turno que ya no existe.</summary>
    MissingShift = 6
}

public sealed record AdjustmentFinding(AdjustmentFindingKind Kind, string EmployeeId, DateOnly Date, string Detail);

public sealed record AdjustmentDiagnosticReport(
    int AdjustmentsInspected,
    int Preservable,
    IReadOnlyList<AdjustmentFinding> Findings);

/// <summary>
/// F05-08: diagnóstico revisable de decisiones manuales heredadas. Sólo clasifica: no modifica datos ni
/// infiere la intención de RH a partir de un resultado calculado ambiguo.
/// </summary>
public interface IAdjustmentDiagnosticService
{
    Task<AdjustmentDiagnosticReport> DiagnoseAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
