using AttendanceSystem.Application.Common;
using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;

public sealed class DeleteShiftCommandHandler : IRequestHandler<DeleteShiftCommand, Result>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IShiftRosterRepository? _rosterRepository;
    private readonly IAttendanceAdjustmentRepository? _adjustmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteShiftCommandHandler(
        IShiftRepository shiftRepository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork,
        IShiftRosterRepository? rosterRepository = null,
        IAttendanceAdjustmentRepository? adjustmentRepository = null)
    {
        _shiftRepository = shiftRepository;
        _employeeRepository = employeeRepository;
        _rosterRepository = rosterRepository;
        _adjustmentRepository = adjustmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await _shiftRepository.GetByIdAsync(request.ShiftId, cancellationToken);

        if (shift is null)
        {
            return Result.Failure($"Turno no encontrado: {request.ShiftId}");
        }

        // Caso 10: un horario en uso no se elimina, para que ningún día (pasado o futuro) se quede sin turno.
        if (await _employeeRepository.IsShiftInUseAsync(request.ShiftId, cancellationToken))
        {
            return Result.Failure("No se puede eliminar el turno porque está asignado a uno o más empleados (como horario actual o en su historial de vigencias).");
        }

        // Decisión 5: un horario asignado a un día de un turno mixto no se elimina.
        var usedBy = (await _shiftRepository.GetAllAsync(cancellationToken))
            .Where(s => s.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Mixto && s.Days.Any(d => d.DayShiftId == request.ShiftId))
            .Select(s => s.Name)
            .ToList();
        if (usedBy.Count > 0)
        {
            return Result.Failure($"No se puede eliminar el turno porque lo usan los turnos mixtos: {string.Join(", ", usedBy)}.");
        }

        if (_rosterRepository != null && await _rosterRepository.CountByShiftAsync(request.ShiftId, cancellationToken) is var rosterDays and > 0)
        {
            return Result.Failure($"No se puede eliminar el turno porque lo usan {rosterDays} día(s) del roster rotativo.");
        }

        if (_adjustmentRepository != null && await _adjustmentRepository.CountByShiftOverrideAsync(request.ShiftId, cancellationToken) is var adjustments and > 0)
        {
            return Result.Failure($"No se puede eliminar el turno porque lo usan {adjustments} ajuste(s) manuales de RH.");
        }

        _shiftRepository.Delete(shift);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
