using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Decisions;

/// <summary>
/// Resultado de una decisión manual (F05-07): la revisión resultante y si la jornada ya refleja la decisión
/// o queda pendiente de consolidación (la solicitud durable garantiza que se aplicará).
/// </summary>
public sealed record AttendanceDecisionOutcome(int Revision, bool Consolidated, string? PendingReason)
{
    public string UserMessage => Consolidated
        ? "Decisión guardada y aplicada."
        : $"Decisión guardada; la jornada está pendiente de consolidación ({PendingReason}).";
}

/// <summary>
/// Contexto de una decisión: el ajuste de la jornada objetivo y, si la operación lo requiere (mover un
/// marcaje desde otra jornada), los ajustes de otras jornadas del mismo empleado, todos bajo el mismo bloqueo.
/// </summary>
public sealed class AttendanceDecisionContext
{
    private readonly IAttendanceAdjustmentRepository _repository;
    private readonly EmployeeId _employeeId;
    private readonly string _author;
    internal readonly Dictionary<DateTime, (AttendanceAdjustment Adjustment, bool IsNew, string Before, int Revision)> Touched = new();

    internal AttendanceDecisionContext(IAttendanceAdjustmentRepository repository, EmployeeId employeeId, string author)
    {
        _repository = repository;
        _employeeId = employeeId;
        _author = author;
    }

    public string Author => _author;

    internal readonly HashSet<DateTime> ExtraAffected = new();

    /// <summary>Jornada que debe recalcularse aunque su propio ajuste no cambie (p. ej. pierde un marcaje movido).</summary>
    public void MarkAffected(DateTime date) => ExtraAffected.Add(date.Date);

    public async Task<AttendanceAdjustment> GetAdjustmentAsync(DateTime date, CancellationToken cancellationToken)
    {
        if (Touched.TryGetValue(date.Date, out var touched)) return touched.Adjustment;

        var existing = await _repository.GetByEmployeeAndDateAsync(_employeeId, date.Date, cancellationToken);
        var adjustment = existing ?? AttendanceAdjustment.Create(_employeeId, date.Date, _author);
        Touched[date.Date] = (adjustment, existing == null, adjustment.Describe(), adjustment.Revision);
        return adjustment;
    }

    internal IEnumerable<DateTime> Dates => Touched.Keys;
}

/// <summary>
/// Protocolo único de decisiones manuales de RH (F05-02 / F05-06 / F05-07), usado por asignación,
/// vacío forzado, restauración, descanso, turno, salidas temporales y extras:
/// <list type="number">
/// <item>Bajo el bloqueo del empleado (fase 03) carga el ajuste y comprueba la revisión esperada: dos
/// operadores sobre la misma versión obtienen un resultado determinista o un conflicto visible.</item>
/// <item>Aplica la decisión y la audita (autor autenticado, motivo, antes/después, fecha y revisión).</item>
/// <item>Registra la solicitud de consolidación durable en la misma transacción.</item>
/// <item>Tras confirmar, recalcula de inmediato; si no es posible, informa que queda pendiente.</item>
/// </list>
/// La decisión es la fuente de verdad: el cálculo la aplica; ningún comando modifica DailyAttendance a mano.
/// </summary>
public sealed class AttendanceDecisionService
{
    private readonly IAttendanceAdjustmentRepository _adjustments;
    private readonly IAttendanceConsolidationRepository _consolidations;
    private readonly IConsolidationWakeup _wakeup;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceCalculationLockService _lockService;
    private readonly ISender _sender;
    private readonly ICurrentUserService? _currentUser;
    private readonly IPersistenceFailureClassifier _classifier;
    private readonly ILogger<AttendanceDecisionService> _logger;
    private readonly AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? _closures;

    public AttendanceDecisionService(
        IAttendanceAdjustmentRepository adjustments,
        IAttendanceConsolidationRepository consolidations,
        IConsolidationWakeup wakeup,
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        ISender sender,
        ILogger<AttendanceDecisionService> logger,
        ICurrentUserService? currentUser = null,
        IPersistenceFailureClassifier? classifier = null,
        AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? closures = null)
    {
        _closures = closures;
        _adjustments = adjustments;
        _consolidations = consolidations;
        _wakeup = wakeup;
        _unitOfWork = unitOfWork;
        _lockService = lockService;
        _sender = sender;
        _logger = logger;
        _currentUser = currentUser;
        _classifier = classifier ?? DefaultPersistenceFailureClassifier.Instance;
    }

    public string Author => _currentUser is { IsAuthenticated: true, UserName: { Length: > 0 } name } ? name : "Sistema";

    public async Task<Result<AttendanceDecisionOutcome>> DecideAsync(
        EmployeeId employeeId,
        DateTime date,
        string action,
        string? reason,
        int? expectedRevision,
        Func<AttendanceDecisionContext, AttendanceAdjustment, Task<Result>> decide,
        CancellationToken cancellationToken)
    {
        // Caso 17: un día de un periodo cerrado no acepta decisiones.
        if (_closures != null && await _closures.DenyAsync(date, cancellationToken) is { } closedError)
        {
            return Result<AttendanceDecisionOutcome>.Failure(closedError);
        }

        var author = Author;
        int revision = 0;
        List<DateTime> affected = new();

        Result written;
        try
        {
            written = await ExclusiveAttendanceWrite.RunAsync(
                _unitOfWork,
                _lockService,
                new[] { employeeId },
                async () =>
                {
                    var context = new AttendanceDecisionContext(_adjustments, employeeId, author);
                    var adjustment = await context.GetAdjustmentAsync(date, cancellationToken);

                    if (expectedRevision.HasValue && adjustment.Revision != expectedRevision.Value)
                    {
                        return Result.Failure(
                            $"La decisión de la jornada cambió mientras se editaba (revisión {adjustment.Revision}, se esperaba {expectedRevision.Value}). Recargue e intente de nuevo.");
                    }

                    var decided = await decide(context, adjustment);
                    if (decided.IsFailure) return decided;

                    if (_closures != null)
                    {
                        foreach (var day in context.Touched.Keys.Concat(context.ExtraAffected))
                        {
                            if (await _closures.DenyAsync(day, cancellationToken) is { } touchedClosed) return Result.Failure(touchedClosed);
                        }
                    }

                    foreach (var (day, (touched, isNew, before, previousRevision)) in context.Touched)
                    {
                        if (touched.Revision == previousRevision) continue; // sin cambios en esta jornada
                        if (isNew) await _adjustments.AddAsync(touched, cancellationToken);
                        else _adjustments.Update(touched);
                        await _adjustments.AddHistoryAsync(AttendanceAdjustmentHistory.Record(touched, action, before, reason, author), cancellationToken);
                        affected.Add(day);
                    }

                    if (affected.Count == 0)
                    {
                        return Result.Failure("La operación no cambia ninguna decisión.");
                    }
                    affected.AddRange(context.ExtraAffected.Where(d => !affected.Contains(d)));

                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    // F05-07: la consolidación de las jornadas afectadas (y sus dependencias) queda durable con la decisión.
                    await _consolidations.RequestAsync(employeeId, affected.Min(), affected.Max(), cancellationToken);

                    revision = adjustment.Revision;
                    return Result.Success();
                },
                cancellationToken);
        }
        catch (Exception ex) when (_classifier.Classify(ex).Kind == PersistenceFailureKind.ConcurrencyConflict)
        {
            return Result<AttendanceDecisionOutcome>.Failure("La jornada cambió mientras se editaba (conflicto de concurrencia). Recargue e intente de nuevo.");
        }
        catch (Exception ex) when (_classifier.Classify(ex).Kind == PersistenceFailureKind.LockTimeout)
        {
            return Result<AttendanceDecisionOutcome>.Failure("La jornada está siendo recalculada por otro proceso. Intente de nuevo en unos segundos.");
        }

        if (written.IsFailure) return Result<AttendanceDecisionOutcome>.Failure(written.Error);

        _logger.LogInformation("Decisión {Action} de {Author} sobre {EmployeeId} {Dates} (revisión {Revision}).",
            action, author, employeeId.Value, string.Join(",", affected.Select(d => d.ToString("yyyy-MM-dd"))), revision);
        _wakeup.Signal();

        try
        {
            await _sender.Send(new ProcessDailyAttendanceCommand(affected.Min(), affected.Max(), EmployeeId: employeeId), cancellationToken);
            return Result<AttendanceDecisionOutcome>.Success(new AttendanceDecisionOutcome(revision, true, null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "La decisión se guardó, pero el recálculo inmediato falló; la consolidación durable la aplicará.");
            return Result<AttendanceDecisionOutcome>.Success(new AttendanceDecisionOutcome(revision, false, ex.Message));
        }
    }
}
