using MediatR;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;

public record SetDailyRestDayOverrideCommand(Guid DailyAttendanceId, bool IsRestDay) : IRequest<bool>;
