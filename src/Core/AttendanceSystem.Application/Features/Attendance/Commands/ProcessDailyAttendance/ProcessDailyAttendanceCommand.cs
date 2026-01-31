using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

public record ProcessDailyAttendanceCommand(DateTime StartDate, DateTime EndDate, BranchId? BranchId = null, EmployeeId? EmployeeId = null) : IRequest<int>;
