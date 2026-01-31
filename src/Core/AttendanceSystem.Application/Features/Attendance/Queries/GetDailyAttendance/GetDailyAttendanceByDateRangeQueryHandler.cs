using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;

public class GetDailyAttendanceByDateRangeQueryHandler 
    : IRequestHandler<GetDailyAttendanceByDateRangeQuery, IReadOnlyList<DailyAttendance>>
{
    private readonly IDailyAttendanceRepository _repository;

    public GetDailyAttendanceByDateRangeQueryHandler(IDailyAttendanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<DailyAttendance>> Handle(
        GetDailyAttendanceByDateRangeQuery request, 
        CancellationToken cancellationToken)
    {
        return await _repository.GetByDateRangeAsync(
            request.StartDate, 
            request.EndDate, 
            request.BranchId, 
            request.EmployeeId, 
            cancellationToken);
    }
}
