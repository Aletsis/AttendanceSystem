namespace AttendanceSystem.Application.Features.Attendance.Queries.GetEmployeeAttendance;

public sealed record GetEmployeeAttendanceQuery(
    string EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<Result<IReadOnlyList<AttendanceRecordDto>>>;

// Handler
public sealed class GetEmployeeAttendanceQueryHandler 
    : IRequestHandler<GetEmployeeAttendanceQuery, Result<IReadOnlyList<AttendanceRecordDto>>>
{
    private readonly IAttendanceRepository _repository;

    public GetEmployeeAttendanceQueryHandler(IAttendanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<AttendanceRecordDto>>> Handle(
        GetEmployeeAttendanceQuery query, 
        CancellationToken cancellationToken)
    {
        EmployeeId? employeeId = null;
        if (!string.IsNullOrWhiteSpace(query.EmployeeId))
        {
            employeeId = EmployeeId.From(query.EmployeeId);
        }
        
        var records = await _repository.GetByDateRangeAsync(
            query.StartDate, 
            query.EndDate,
            employeeId,
            cancellationToken);

        var dtos = records.Select(r => new AttendanceRecordDto(
            Id: r.Id.Value,
            EmployeeId: r.EmployeeId.Value,
            DeviceId: r.DeviceId.Value,
            CheckTime: r.CheckTime,
            VerifyMethod: r.VerifyMethod.Name,
            CheckType: r.CheckType.Name,
            Status: r.Status.ToString()
        )).ToList();

        return Result<IReadOnlyList<AttendanceRecordDto>>.Success(dtos);
    }
}