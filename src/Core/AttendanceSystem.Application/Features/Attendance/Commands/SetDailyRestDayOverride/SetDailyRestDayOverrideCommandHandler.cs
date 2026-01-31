using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;

public class SetDailyRestDayOverrideCommandHandler : IRequestHandler<SetDailyRestDayOverrideCommand, bool>
{
    private readonly IDailyAttendanceRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public SetDailyRestDayOverrideCommandHandler(IDailyAttendanceRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(SetDailyRestDayOverrideCommand request, CancellationToken cancellationToken)
    {
        var id = DailyAttendanceId.From(request.DailyAttendanceId);
        var attendance = await _repository.GetByIdAsync(id, cancellationToken);

        if (attendance == null) return false;

        attendance.SetRestDayOverride(request.IsRestDay);

        _repository.Update(attendance);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
