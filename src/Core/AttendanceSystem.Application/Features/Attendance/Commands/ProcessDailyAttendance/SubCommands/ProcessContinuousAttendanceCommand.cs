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
    bool IsRestDay) : IRequest;

public class ProcessContinuousAttendanceCommandHandler : IRequestHandler<ProcessContinuousAttendanceCommand>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILogger<ProcessContinuousAttendanceCommandHandler> _logger;

    public ProcessContinuousAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        ILogger<ProcessContinuousAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _logger = logger;
    }

    public async Task Handle(ProcessContinuousAttendanceCommand request, CancellationToken cancellationToken)
    {
        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceRecord? checkInRecord = null;
        AttendanceRecord? checkOutRecord = null;

        var potentialIn = request.Records
            .Where(r => r.CheckTime.Date == request.Date.Date && r.Status == AttendanceStatus.Pending)
            .OrderBy(r => r.CheckTime)
            .FirstOrDefault();

        if (potentialIn != null)
        {
            checkInRecord = potentialIn;
            checkIn = potentialIn.CheckTime;

            checkOutRecord = request.Records
                .Where(r => r.CheckTime > checkIn.Value && (r.CheckTime - checkIn.Value).TotalHours <= 24)
                .OrderBy(r => r.CheckTime)
                .FirstOrDefault();

            if (checkOutRecord != null)
                checkOut = checkOutRecord.CheckTime;
        }

        if (checkInRecord != null)
        {
            checkInRecord.MarkAsProcessed();
            checkInRecord.SetInferredType(CheckType.CheckIn);
            await _attendanceRepo.UpdateAsync(checkInRecord, cancellationToken);
        }

        if (checkOutRecord != null)
        {
            checkOutRecord.MarkAsProcessed();
            checkOutRecord.SetInferredType(CheckType.CheckOut);
            await _attendanceRepo.UpdateAsync(checkOutRecord, cancellationToken);
        }

        var dailyAttendance = DailyAttendance.Create(
            request.Employee.Id,
            request.Date,
            request.Shift,
            checkIn,
            checkOut,
            request.IsRestDay,
            checkInRecord?.Id,
            checkOutRecord?.Id,
            request.Employee.CalculateOvertimeBeforeEntry,
            request.Employee.OvertimeAuthorized);

        _dailyRepo.Add(dailyAttendance);
    }
}
