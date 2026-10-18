using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;

public class SetDailyRestDayOverrideCommandHandler : IRequestHandler<SetDailyRestDayOverrideCommand, bool>
{
    private readonly IDailyAttendanceRepository _repository;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepo;
    private readonly IUnitOfWork _unitOfWork;

    public SetDailyRestDayOverrideCommandHandler(
        IDailyAttendanceRepository repository,
        IAttendanceAdjustmentRepository adjustmentRepo,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _adjustmentRepo = adjustmentRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(SetDailyRestDayOverrideCommand request, CancellationToken cancellationToken)
    {
        var id = DailyAttendanceId.From(request.DailyAttendanceId);
        var attendance = await _repository.GetByIdAsync(id, cancellationToken);

        if (attendance == null) return false;

        attendance.SetRestDayOverride(request.IsRestDay);

        var adjustment = await _adjustmentRepo.GetByEmployeeAndDateAsync(attendance.EmployeeId, attendance.Date, cancellationToken);
        if (adjustment == null)
        {
            adjustment = AttendanceAdjustment.Create(attendance.EmployeeId, attendance.Date, "Usuario RH");
            adjustment.SetRestDayOverride(request.IsRestDay, "Usuario RH", "Ajuste manual de descanso");
            await _adjustmentRepo.AddAsync(adjustment, cancellationToken);
        }
        else
        {
            adjustment.SetRestDayOverride(request.IsRestDay, "Usuario RH", "Ajuste manual de descanso");
            _adjustmentRepo.Update(adjustment);
        }

        _repository.Update(attendance);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
