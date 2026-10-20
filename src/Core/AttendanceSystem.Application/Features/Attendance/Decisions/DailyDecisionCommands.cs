using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Decisions;

/// <summary>
/// Clasificación manual de las salidas temporales de una jornada (permiso, sin goce, etc.).
/// <c>Status</c> nulo retira la decisión y vuelve al resultado calculado.
/// </summary>
public sealed record ClassifyTemporaryExitCommand(
    string EmployeeId,
    DateOnly Date,
    TemporaryExitStatus? Status,
    string? Note = null,
    int? ExpectedRevision = null) : IRequest<Result<AttendanceDecisionOutcome>>;

public sealed class ClassifyTemporaryExitCommandHandler : IRequestHandler<ClassifyTemporaryExitCommand, Result<AttendanceDecisionOutcome>>
{
    private readonly AttendanceDecisionService _decisions;

    public ClassifyTemporaryExitCommandHandler(AttendanceDecisionService decisions) => _decisions = decisions;

    public Task<Result<AttendanceDecisionOutcome>> Handle(ClassifyTemporaryExitCommand request, CancellationToken cancellationToken) =>
        _decisions.DecideAsync(
            EmployeeId.From(request.EmployeeId),
            request.Date.ToDateTime(TimeOnly.MinValue),
            "SalidaTemporal",
            request.Note,
            request.ExpectedRevision,
            (context, adjustment) =>
            {
                if (request.Status is { } status)
                    adjustment.SetTemporaryExitClassification(status, request.Note ?? status.ToString(), context.Author);
                else
                    adjustment.ClearTemporaryExitClassification(context.Author, "Clasificación retirada");
                return Task.FromResult(Result.Success());
            },
            cancellationToken);
}

/// <summary>
/// Autorización de horas extras de una jornada. Ambos valores nulos retiran la decisión y vuelven a la
/// configuración del empleado.
/// </summary>
public sealed record SetDailyOvertimeAuthorizationCommand(
    string EmployeeId,
    DateOnly Date,
    bool? OvertimeAuthorized,
    bool? CalculateOvertimeBeforeEntry,
    string? Reason = null,
    int? ExpectedRevision = null) : IRequest<Result<AttendanceDecisionOutcome>>;

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

        return _decisions.DecideAsync(
            EmployeeId.From(request.EmployeeId),
            request.Date.ToDateTime(TimeOnly.MinValue),
            "HorasExtras",
            request.Reason,
            request.ExpectedRevision,
            (context, adjustment) =>
            {
                if (request.OvertimeAuthorized is { } authorized)
                    adjustment.SetOvertimeConfiguration(authorized, request.CalculateOvertimeBeforeEntry!.Value, context.Author);
                else
                    adjustment.ClearOvertimeConfiguration(context.Author, "Autorización de extras retirada");
                return Task.FromResult(Result.Success());
            },
            cancellationToken);
    }
}
