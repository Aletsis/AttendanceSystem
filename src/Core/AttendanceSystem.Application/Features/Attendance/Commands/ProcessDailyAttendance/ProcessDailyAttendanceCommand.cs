using AttendanceSystem.Domain.Services;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

/// <param name="EmployeeIds">F07-08: conjunto de empleados (p. ej. las solicitudes reclamadas por el consumidor).</param>
public record ProcessDailyAttendanceCommand(
    DateTime StartDate,
    DateTime EndDate,
    BranchId? BranchId = null,
    EmployeeId? EmployeeId = null,
    IReadOnlyCollection<EmployeeId>? EmployeeIds = null) : IRequest<int>, IDateRangeRequest
{
    int IDateRangeRequest.MaxRangeDays => DateRangePolicy.MaxRecalculationDays;
}
