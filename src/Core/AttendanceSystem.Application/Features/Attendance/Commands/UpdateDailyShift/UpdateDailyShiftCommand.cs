using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Decisions;

namespace AttendanceSystem.Application.Features.Attendance.Commands.UpdateDailyShift;

public sealed record UpdateDailyShiftCommand(
    string EmployeeId,
    DateOnly Date,
    Guid ShiftId) : IRequest<Result>;

/// <summary>
/// Cambia el turno de una jornada como decisión manual persistente (F05-02) y la reconstruye con el motor
/// común de recálculo (F02-05).
/// </summary>
public sealed class UpdateDailyShiftCommandHandler : IRequestHandler<UpdateDailyShiftCommand, Result>
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly AttendanceDecisionService _decisions;

    public UpdateDailyShiftCommandHandler(
        IShiftRepository shiftRepo,
        IEmployeeRepository employeeRepo,
        AttendanceDecisionService decisions)
    {
        _shiftRepo = shiftRepo;
        _employeeRepo = employeeRepo;
        _decisions = decisions;
    }

    public async Task<Result> Handle(UpdateDailyShiftCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var shiftId = ShiftId.From(request.ShiftId);

        var employee = await _employeeRepo.GetByIdAsync(employeeId, cancellationToken);
        if (employee == null) return Result.Failure("Empleado no encontrado.");

        var shift = await _shiftRepo.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            return Result.Failure("El turno seleccionado no existe.");
        }

        // Decisión persistente y auditada; el motor común reconstruye la jornada y sus dependencias.
        var result = await _decisions.DecideAsync(
            employeeId,
            request.Date.ToDateTime(TimeOnly.MinValue),
            "Turno",
            $"Cambio manual de turno a {shift.Name}",
            expectedRevision: null,
            (context, adjustment) =>
            {
                adjustment.SetShiftOverride(shiftId, context.Author, "Cambio manual de turno");
                return Task.FromResult(Result.Success());
            },
            cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
