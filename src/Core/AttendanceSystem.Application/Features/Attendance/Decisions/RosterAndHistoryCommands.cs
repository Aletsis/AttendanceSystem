using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Decisions;

/// <summary>
/// Caso 13: retira el cambio manual de turno del día; la jornada vuelve a seguir la plantilla o el roster del empleado.
/// </summary>
[RequiresPermission(Permissions.AttendanceAdjust)]
public sealed record ClearDailyShiftOverrideCommand(
    string EmployeeId,
    DateOnly Date,
    string? Reason = null,
    int? ExpectedRevision = null) : IRequest<Result<AttendanceDecisionOutcome>>;

public sealed class ClearDailyShiftOverrideCommandHandler : IRequestHandler<ClearDailyShiftOverrideCommand, Result<AttendanceDecisionOutcome>>
{
    private readonly AttendanceDecisionService _decisions;

    public ClearDailyShiftOverrideCommandHandler(AttendanceDecisionService decisions) => _decisions = decisions;

    public Task<Result<AttendanceDecisionOutcome>> Handle(ClearDailyShiftOverrideCommand request, CancellationToken cancellationToken) =>
        _decisions.DecideAsync(
            EmployeeId.From(request.EmployeeId),
            request.Date.ToDateTime(TimeOnly.MinValue),
            "RegresarTurnoRoster",
            request.Reason,
            request.ExpectedRevision,
            (context, adjustment) =>
            {
                if (adjustment.ShiftIdOverride == null) return Task.FromResult(Result.Failure("El día no tiene un cambio manual de turno."));
                adjustment.ClearShiftOverride(context.Author, request.Reason ?? "Regresar al horario del roster");
                return Task.FromResult(Result.Success());
            },
            cancellationToken);
}

/// <summary>
/// Caso 13: retira la marca manual de descanso o laborable del día; vuelven a regir los descansos del empleado o su roster.
/// </summary>
[RequiresPermission(Permissions.AttendanceAdjust)]
public sealed record ClearDailyRestDayOverrideCommand(
    string EmployeeId,
    DateOnly Date,
    string? Reason = null,
    int? ExpectedRevision = null) : IRequest<Result<AttendanceDecisionOutcome>>;

public sealed class ClearDailyRestDayOverrideCommandHandler : IRequestHandler<ClearDailyRestDayOverrideCommand, Result<AttendanceDecisionOutcome>>
{
    private readonly AttendanceDecisionService _decisions;

    public ClearDailyRestDayOverrideCommandHandler(AttendanceDecisionService decisions) => _decisions = decisions;

    public Task<Result<AttendanceDecisionOutcome>> Handle(ClearDailyRestDayOverrideCommand request, CancellationToken cancellationToken) =>
        _decisions.DecideAsync(
            EmployeeId.From(request.EmployeeId),
            request.Date.ToDateTime(TimeOnly.MinValue),
            "RegresarDescansoRoster",
            request.Reason,
            request.ExpectedRevision,
            (context, adjustment) =>
            {
                if (adjustment.IsRestDayOverride == null) return Task.FromResult(Result.Failure("El día no tiene una marca manual de descanso."));
                adjustment.ClearRestDayOverride(context.Author, request.Reason ?? "Regresar al descanso del roster");
                return Task.FromResult(Result.Success());
            },
            cancellationToken);
}

/// <summary>
/// Caso 13/14: anula un marcaje manual (sólo backend, sin pantalla todavía). Los marcajes son inmutables: no se borra,
/// queda anulado con autor y motivo y el cálculo lo ignora. Retira las decisiones que lo usaban, deja el movimiento en
/// el historial y recalcula las jornadas que pudo tocar (la previa si el turno cruza la medianoche).
/// </summary>
[RequiresPermission(Permissions.AttendanceVoidManualPunch)]
public sealed record VoidManualAttendanceCommand(string RecordId, string? Reason) : IRequest<Result>;

public sealed class VoidManualAttendanceCommandHandler : IRequestHandler<VoidManualAttendanceCommand, Result>
{
    private readonly IAttendanceRepository _records;
    private readonly IAttendanceAdjustmentRepository _adjustments;
    private readonly IAttendanceConsolidationRepository _consolidations;
    private readonly IConsolidationWakeup _wakeup;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceCalculationLockService _lockService;
    private readonly ISender _sender;
    private readonly ICurrentUserService? _currentUser;
    private readonly ILogger<VoidManualAttendanceCommandHandler> _logger;

    public VoidManualAttendanceCommandHandler(
        IAttendanceRepository records,
        IAttendanceAdjustmentRepository adjustments,
        IAttendanceConsolidationRepository consolidations,
        IConsolidationWakeup wakeup,
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        ISender sender,
        ILogger<VoidManualAttendanceCommandHandler> logger,
        ICurrentUserService? currentUser = null)
    {
        _records = records;
        _adjustments = adjustments;
        _consolidations = consolidations;
        _wakeup = wakeup;
        _unitOfWork = unitOfWork;
        _lockService = lockService;
        _sender = sender;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(VoidManualAttendanceCommand request, CancellationToken cancellationToken)
    {
        if (AdjustmentReasons.Validate(AdjustmentReasons.VoidManualPunch, request.Reason) is { } missingReason)
        {
            return Result.Failure(missingReason);
        }

        var record = await _records.GetByIdAsync(AttendanceRecordId.From(request.RecordId), cancellationToken);
        if (record == null) return Result.Failure("Marcaje no encontrado.");
        if (!record.IsManual) return Result.Failure("Sólo se pueden anular marcajes manuales.");
        if (record.IsVoided) return Result.Failure("El marcaje ya está anulado.");

        var author = _currentUser is { IsAuthenticated: true, UserName: { Length: > 0 } name } ? name : "Sistema";
        var workDate = record.GetWorkDate().ToDateTime(TimeOnly.MinValue);
        var from = workDate.AddDays(-1);
        var to = workDate.AddDays(1);
        var civil = record.GetCivilTime();

        var written = await ExclusiveAttendanceWrite.RunAsync(_unitOfWork, _lockService, new[] { record.EmployeeId }, async () =>
        {
            // Las decisiones que asignaban o excluían el marcaje dejan de aplicar.
            foreach (var adjustment in await _adjustments.GetByDateRangeAsync(from, to, employeeId: record.EmployeeId, cancellationToken: cancellationToken))
            {
                var before = adjustment.Describe();
                if (!adjustment.RestoreAutomatic(record.Id, author, request.Reason)) continue;
                _adjustments.Update(adjustment);
                await _adjustments.AddHistoryAsync(
                    AttendanceAdjustmentHistory.Record(adjustment, AdjustmentReasons.VoidManualPunch, before, request.Reason, author), cancellationToken);
            }

            record.Void(author, request.Reason!);
            await _records.UpdateAsync(record, cancellationToken);
            await _adjustments.AddHistoryAsync(AttendanceAdjustmentHistory.Event(
                record.EmployeeId, workDate, AdjustmentReasons.VoidManualPunch,
                $"marcaje manual {civil:dd/MM/yyyy HH:mm} (registró {record.RegisteredBy ?? "sin dato"}: {record.Reason ?? "sin motivo"})",
                "anulado", request.Reason, author), cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _consolidations.RequestAsync(record.EmployeeId, from, to, cancellationToken);
            return Result.Success();
        }, cancellationToken);
        if (written.IsFailure) return written;

        _logger.LogInformation("Marcaje manual {RecordId} de {EmployeeId} anulado por {Author}.", request.RecordId, record.EmployeeId.Value, author);
        _wakeup.Signal();
        try
        {
            await _sender.Send(new ProcessDailyAttendanceCommand(from, to, EmployeeId: record.EmployeeId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "El marcaje se anuló, pero el recálculo inmediato falló; la consolidación durable lo aplicará.");
        }
        return Result.Success();
    }
}

/// <summary>Un movimiento del historial de una jornada (caso 13).</summary>
public sealed record AttendanceHistoryEntryDto(
    DateTime ChangedAt,
    string ChangedBy,
    string Action,
    string? Reason,
    string Before,
    string After);

/// <summary>Caso 13: historial de ajustes de la jornada de un empleado, del más reciente al más antiguo.</summary>
[RequiresPermission(Permissions.AttendanceViewHistory)]
public sealed record GetAttendanceHistoryQuery(string EmployeeId, DateOnly Date) : IRequest<IReadOnlyList<AttendanceHistoryEntryDto>>;

public sealed class GetAttendanceHistoryQueryHandler : IRequestHandler<GetAttendanceHistoryQuery, IReadOnlyList<AttendanceHistoryEntryDto>>
{
    private readonly IAttendanceAdjustmentRepository _adjustments;

    public GetAttendanceHistoryQueryHandler(IAttendanceAdjustmentRepository adjustments) => _adjustments = adjustments;

    public async Task<IReadOnlyList<AttendanceHistoryEntryDto>> Handle(GetAttendanceHistoryQuery request, CancellationToken cancellationToken)
    {
        var entries = await _adjustments.GetHistoryAsync(EmployeeId.From(request.EmployeeId), request.Date.ToDateTime(TimeOnly.MinValue), cancellationToken);
        return entries
            .OrderByDescending(e => e.ChangedAt).ThenByDescending(e => e.Revision)
            .Select(e => new AttendanceHistoryEntryDto(e.ChangedAt, e.ChangedBy, Describe(e.Action), e.Reason, e.Before, e.After))
            .ToList();
    }

    public static string Describe(string action) => action switch
    {
        "Entrada" or "Entrada B1" => "Asignó la entrada",
        "Salida B1" => "Asignó la salida a comida (B1)",
        "Entrada B2" => "Asignó el regreso de comida (B2)",
        "Salida" or "Salida B2" => "Asignó la salida",
        "None" => "Desasignó un marcaje",
        "Restaurar" => "Restauró el cálculo automático",
        "Turno" => "Cambió el horario del día",
        "RegresarTurnoRoster" => "Regresó al horario del roster",
        "Descanso" => "Cambió el descanso del día",
        "RegresarDescansoRoster" => "Regresó al descanso del roster",
        "SalidaTemporal" => "Clasificó una salida temporal",
        "HorasExtras" => "Cambió la autorización de extras",
        AdjustmentReasons.ManualPunch => "Agregó un marcaje manual",
        AdjustmentReasons.VoidManualPunch => "Anuló un marcaje manual",
        _ => action
    };
}

/// <summary>Caso 13: decisiones manuales vigentes del día que pueden regresarse al roster.</summary>
public sealed record DayAdjustmentStateDto(string? ShiftOverrideName, bool? IsRestDayOverride, int HistoryCount, int Revision);

[RequiresPermission(Permissions.AttendanceView)]
public sealed record GetDayAdjustmentStateQuery(string EmployeeId, DateOnly Date) : IRequest<DayAdjustmentStateDto>;

public sealed class GetDayAdjustmentStateQueryHandler : IRequestHandler<GetDayAdjustmentStateQuery, DayAdjustmentStateDto>
{
    private readonly IAttendanceAdjustmentRepository _adjustments;
    private readonly IShiftRepository _shifts;

    public GetDayAdjustmentStateQueryHandler(IAttendanceAdjustmentRepository adjustments, IShiftRepository shifts)
    {
        _adjustments = adjustments;
        _shifts = shifts;
    }

    public async Task<DayAdjustmentStateDto> Handle(GetDayAdjustmentStateQuery request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var date = request.Date.ToDateTime(TimeOnly.MinValue);
        var adjustment = await _adjustments.GetByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        var history = await _adjustments.GetHistoryAsync(employeeId, date, cancellationToken);

        string? shiftName = null;
        if (adjustment?.ShiftIdOverride is { } shiftId)
        {
            shiftName = (await _shifts.GetByIdAsync(shiftId, cancellationToken))?.Name ?? "horario eliminado";
        }
        return new DayAdjustmentStateDto(shiftName, adjustment?.IsRestDayOverride, history.Count, adjustment?.Revision ?? 0);
    }
}
