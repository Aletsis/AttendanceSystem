using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Decisions;

namespace AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;

/// <summary>
/// Marca o desmarca la jornada como descanso con el protocolo común de decisiones (F05-02): se guarda en
/// el ajuste, se audita y el cálculo la aplica. Si un recálculo reemplazó la jornada mostrada en pantalla
/// (identificador distinto), no se aplica y se devuelve el fallo para recargar.
/// </summary>
public class SetDailyRestDayOverrideCommandHandler : IRequestHandler<SetDailyRestDayOverrideCommand, Result<AttendanceDecisionOutcome>>
{
    private readonly IDailyAttendanceRepository _repository;
    private readonly AttendanceDecisionService _decisions;

    public SetDailyRestDayOverrideCommandHandler(IDailyAttendanceRepository repository, AttendanceDecisionService decisions)
    {
        _repository = repository;
        _decisions = decisions;
    }

    public async Task<Result<AttendanceDecisionOutcome>> Handle(SetDailyRestDayOverrideCommand request, CancellationToken cancellationToken)
    {
        var id = DailyAttendanceId.From(request.DailyAttendanceId);
        var located = await _repository.GetByIdAsync(id, cancellationToken);
        if (located == null) return Result<AttendanceDecisionOutcome>.Failure("La jornada se recalculó mientras se editaba. Recargue e intente de nuevo.");

        return await _decisions.DecideAsync(
            located.EmployeeId,
            located.Date,
            "Descanso",
            request.Reason ?? (request.IsRestDay ? "Marcado como descanso" : "Marcado como laborable"),
            expectedRevision: null,
            async (context, adjustment) =>
            {
                var current = await _repository.GetByEmployeeAndDateAsync(located.EmployeeId, located.Date, cancellationToken);
                if (current == null || current.Id != id)
                {
                    return Result.Failure("La jornada se recalculó mientras se editaba. Recargue e intente de nuevo.");
                }

                adjustment.SetRestDayOverride(request.IsRestDay, context.Author, "Ajuste manual de descanso");
                return Result.Success();
            },
            cancellationToken);
    }
}
