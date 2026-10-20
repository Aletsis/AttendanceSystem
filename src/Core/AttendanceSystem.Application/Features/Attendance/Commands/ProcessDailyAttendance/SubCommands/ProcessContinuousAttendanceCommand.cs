using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessContinuousAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    bool IsAutoDetectedShift = false,
    AttendanceAdjustment? Adjustment = null,
    string? TimeZoneId = null,
    PunchAssignmentDraft? Draft = null,
    DateTime? NextJornadaEntryStart = null) : IRequest;

public class ProcessContinuousAttendanceCommandHandler : IRequestHandler<ProcessContinuousAttendanceCommand>
{
    private readonly ISender _sender;

    public ProcessContinuousAttendanceCommandHandler(ISender sender)
    {
        _sender = sender;
    }

    public async Task Handle(ProcessContinuousAttendanceCommand request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ProcessFlexibleAttendanceCommand(
            request.Employee,
            request.Date,
            request.Shift,
            request.Records,
            request.IsRestDay,
            request.IsAutoDetectedShift,
            Adjustment: request.Adjustment,
            TimeZoneId: request.TimeZoneId,
            Draft: request.Draft,
            NextJornadaEntryStart: request.NextJornadaEntryStart,
            Continuous: true), cancellationToken);
    }
}
