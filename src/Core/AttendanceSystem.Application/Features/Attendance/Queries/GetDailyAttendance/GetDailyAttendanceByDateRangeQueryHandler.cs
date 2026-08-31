using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;

public class GetDailyAttendanceByDateRangeQueryHandler 
    : IRequestHandler<GetDailyAttendanceByDateRangeQuery, IReadOnlyList<DailyAttendance>>
{
    private readonly IDailyAttendanceRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;

    public GetDailyAttendanceByDateRangeQueryHandler(
        IDailyAttendanceRepository repository,
        IEmployeeRepository employeeRepository)
    {
        _repository = repository;
        _employeeRepository = employeeRepository;
    }

    public async Task<IReadOnlyList<DailyAttendance>> Handle(
        GetDailyAttendanceByDateRangeQuery request, 
        CancellationToken cancellationToken)
    {
        var records = await _repository.GetByDateRangeAsync(
            request.StartDate, 
            request.EndDate, 
            request.BranchId, 
            request.EmployeeId, 
            cancellationToken);

        var employees = await _employeeRepository.GetAllAsync(cancellationToken);
        var empDict = employees.ToDictionary(e => e.Id, e => e);

        return records
            .Where(r => empDict.TryGetValue(r.EmployeeId, out var emp) && r.Date.Date >= emp.HireDate.Date)
            .ToList();
    }
}
