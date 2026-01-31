using AttendanceSystem.Application.Common;
using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;

internal sealed class DeleteShiftCommandHandler : IRequestHandler<DeleteShiftCommand, Result>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteShiftCommandHandler(
        IShiftRepository shiftRepository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await _shiftRepository.GetByIdAsync(request.ShiftId, cancellationToken);

        if (shift is null)
        {
            return Result.Failure($"Turno no encontrado: {request.ShiftId}");
        }

        // Check if shift is in use
        if (await _employeeRepository.IsShiftInUseAsync(request.ShiftId, cancellationToken))
        {
             return Result.Failure("No se puede eliminar el turno porque está asignado a uno o más empleados.");
        }

        _shiftRepository.Delete(shift);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
