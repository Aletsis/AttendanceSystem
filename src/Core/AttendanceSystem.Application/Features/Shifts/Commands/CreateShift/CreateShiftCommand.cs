using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;

using MediatR;

namespace AttendanceSystem.Application.Features.Shifts.Commands.CreateShift;

public sealed record CreateShiftCommand(
    string Name,
    TimeSpan StartTime,
    int ToleranceMinutes,
    TimeSpan WorkHours,
    ShiftType ShiftType) : IRequest<Result<Guid>>;

public sealed class CreateShiftCommandHandler : IRequestHandler<CreateShiftCommand, Result<Guid>>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateShiftCommandHandler(IShiftRepository shiftRepository, IUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        try 
        {
            var shift = Shift.Create(
                request.Name,
                request.StartTime,
                request.ToleranceMinutes,
                request.WorkHours,
                request.ShiftType);

            await _shiftRepository.AddAsync(shift, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(shift.Id.Value);
        }
        catch (Exception ex)
        {
            // Providing a generic error for now, relying on global exception handling or specific error construction if needed
             return Result<Guid>.Failure(ex.Message);
        }
    }
}
