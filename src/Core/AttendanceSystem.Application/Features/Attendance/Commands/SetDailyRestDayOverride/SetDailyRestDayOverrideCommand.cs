using MediatR;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;

[RequiresPermission(Permissions.AttendanceAdjust)]
public record SetDailyRestDayOverrideCommand(Guid DailyAttendanceId, bool IsRestDay, string? Reason = null)
    : IRequest<AttendanceSystem.Application.Common.Result<AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionOutcome>>;
