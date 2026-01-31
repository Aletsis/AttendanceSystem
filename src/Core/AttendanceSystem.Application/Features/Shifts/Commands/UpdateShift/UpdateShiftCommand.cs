using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using MediatR;

namespace AttendanceSystem.Application.Features.Shifts.Commands.UpdateShift;

public sealed record UpdateShiftCommand(
    Guid Id,
    string Name,
    TimeSpan StartTime,
    int ToleranceMinutes,
    TimeSpan WorkHours,
    ShiftType ShiftType) : IRequest<Result>;

public sealed class UpdateShiftCommandHandler : IRequestHandler<UpdateShiftCommand, Result>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateShiftCommandHandler(IShiftRepository shiftRepository, IUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateShiftCommand request, CancellationToken cancellationToken)
    {
        var shiftId = ShiftId.From(request.Id);
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);

        if (shift is null)
        {
            return Result.Failure("Turno no encontrado");
        }

        try
        {
            shift.Update(
                request.Name,
                request.StartTime,
                request.ToleranceMinutes,
                request.WorkHours,
                request.ShiftType);

            _shiftRepository.Update(shift);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
             return Result.Failure(ex.Message);
        }
    }
}
