using MediatR;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;

public record GetDailyAttendanceByDateRangeQuery(
    DateTime StartDate, 
    DateTime EndDate,
    BranchId? BranchId = null,
    EmployeeId? EmployeeId = null) 
    : IRequest<IReadOnlyList<DailyAttendance>>;
