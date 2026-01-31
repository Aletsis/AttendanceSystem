using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Attendance.Commands.UpdateDailyShift;

public sealed record UpdateDailyShiftCommand(
    string EmployeeId,
    DateOnly Date,
    Guid ShiftId) : IRequest<Result>;

public sealed class UpdateDailyShiftCommandHandler : IRequestHandler<UpdateDailyShiftCommand, Result>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IUnitOfWork _unitOfWork;
    // We might need to create DailyAttendance if it doesn't exist?
    // If we update the shift, we usually imply we want to process/check attendance against this new shift.
    // If DailyAttendance doesn't exist, we should create it. But we need EmployeeId, Date.
    
    public UpdateDailyShiftCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IShiftRepository shiftRepo,
        IUnitOfWork unitOfWork)
    {
        _dailyRepo = dailyRepo;
        _shiftRepo = shiftRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateDailyShiftCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var shiftId = ShiftId.From(request.ShiftId);

        var shift = await _shiftRepo.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            return Result.Failure("El turno seleccionado no existe.");
        }

        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId,
            request.Date.ToDateTime(TimeOnly.MinValue),
            cancellationToken);

        if (daily == null)
        {
            // Create new DailyAttendance with this shift
            // Use Factory
            daily = DailyAttendance.Create(
                employeeId,
                request.Date.ToDateTime(TimeOnly.MinValue),
                shift,
                null, // CheckIn
                null, // CheckOut
                false // Not Rest Day
            );
            
            _dailyRepo.Add(daily);
        }
        else
        {
            // Update existing
            daily.UpdateShift(shift);
            // We assume EF Core tracks changes
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return Result.Success();
    }
}
