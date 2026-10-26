using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Decisions;

/// <summary>
/// Caso 11: RH clasifica una salida de la jornada (identificada por su marcaje de salida) como comida, permiso con
/// goce, permiso sin goce o error de checada. <c>Status</c> nulo retira la decisión y vuelve a la clasificación
/// automática (comida si la ventana la detectó, si no pendiente).
/// </summary>
[RequiresPermission(Permissions.AttendanceAdjust)]
public sealed record ClassifyTemporaryExitCommand(
    string EmployeeId,
    DateOnly Date,
    Guid ExitRecordId,
    TemporaryExitStatus? Status,
    string? Note = null,
    int? ExpectedRevision = null) : IRequest<Result<AttendanceDecisionOutcome>>;

public sealed class ClassifyTemporaryExitCommandHandler : IRequestHandler<ClassifyTemporaryExitCommand, Result<AttendanceDecisionOutcome>>
{
    private readonly AttendanceDecisionService _decisions;

    public ClassifyTemporaryExitCommandHandler(AttendanceDecisionService decisions) => _decisions = decisions;

    public Task<Result<AttendanceDecisionOutcome>> Handle(ClassifyTemporaryExitCommand request, CancellationToken cancellationToken)
    {
        if (request.Status == TemporaryExitStatus.Pending)
        {
            return Task.FromResult(Result<AttendanceDecisionOutcome>.Failure("Una salida se clasifica como comida, con goce, sin goce o error de checada."));
        }

        return _decisions.DecideAsync(
            EmployeeId.From(request.EmployeeId),
            request.Date.ToDateTime(TimeOnly.MinValue),
            "SalidaTemporal",
            request.Note,
            request.ExpectedRevision,
            (context, adjustment) =>
            {
                adjustment.ClassifyExit(request.ExitRecordId, request.Status, context.Author, request.Note);
                return Task.FromResult(Result.Success());
            },
            cancellationToken);
    }
}

/// <summary>
/// Autorización de horas extras de una jornada. Ambos valores nulos retiran la decisión y vuelven a la
/// configuración del empleado. Caso 12: <paramref name="AuthorizedMinutes"/> es lo que RH autoriza (el excedente
/// completo o bloques de 30 min) del excedente <paramref name="ForRawMinutes"/> que vio; si al recalcular cambia, la
/// autorización deja de aplicar.
/// </summary>
[RequiresPermission(Permissions.AttendanceAdjust)]
public sealed record SetDailyOvertimeAuthorizationCommand(
    string EmployeeId,
    DateOnly Date,
    bool? OvertimeAuthorized,
    bool? CalculateOvertimeBeforeEntry,
    string? Reason = null,
    int? ExpectedRevision = null,
    int? AuthorizedMinutes = null,
    int? ForRawMinutes = null) : IRequest<Result<AttendanceDecisionOutcome>>;

public sealed class SetDailyOvertimeAuthorizationCommandHandler : IRequestHandler<SetDailyOvertimeAuthorizationCommand, Result<AttendanceDecisionOutcome>>
{
    private readonly AttendanceDecisionService _decisions;

    public SetDailyOvertimeAuthorizationCommandHandler(AttendanceDecisionService decisions) => _decisions = decisions;

    public Task<Result<AttendanceDecisionOutcome>> Handle(SetDailyOvertimeAuthorizationCommand request, CancellationToken cancellationToken)
    {
        if (request.OvertimeAuthorized.HasValue != request.CalculateOvertimeBeforeEntry.HasValue)
        {
            return Task.FromResult(Result<AttendanceDecisionOutcome>.Failure("Indique ambos valores de horas extras o ninguno para retirar la decisión."));
        }
        if (request.AuthorizedMinutes.HasValue != request.ForRawMinutes.HasValue ||
            (request.AuthorizedMinutes is { } minutes &&
             (request.OvertimeAuthorized != true || !DailyAttendance.IsValidOvertimeAuthorization(minutes, request.ForRawMinutes!.Value))))
        {
            return Task.FromResult(Result<AttendanceDecisionOutcome>.Failure(DailyAttendance.InvalidOvertimeAuthorizationMessage));
        }

        return _decisions.DecideAsync(
            EmployeeId.From(request.EmployeeId),
            request.Date.ToDateTime(TimeOnly.MinValue),
            "HorasExtras",
            request.Reason,
            request.ExpectedRevision,
            (context, adjustment) =>
            {
                if (request.OvertimeAuthorized is { } authorized)
                    adjustment.SetOvertimeConfiguration(authorized, request.CalculateOvertimeBeforeEntry!.Value, context.Author, request.AuthorizedMinutes, request.ForRawMinutes);
                else
                    adjustment.ClearOvertimeConfiguration(context.Author, "Autorización de extras retirada");
                return Task.FromResult(Result.Success());
            },
            cancellationToken);
    }
}
