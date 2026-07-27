using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessNoShiftAttendanceCommand(
    Employee Employee,
    DateTime Date,
    List<AttendanceRecord> Records,
    bool IsRestDay) : IRequest;

public class ProcessNoShiftAttendanceCommandHandler : IRequestHandler<ProcessNoShiftAttendanceCommand>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILogger<ProcessNoShiftAttendanceCommandHandler> _logger;

    public ProcessNoShiftAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        ILogger<ProcessNoShiftAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _logger = logger;
    }

    public async Task Handle(ProcessNoShiftAttendanceCommand request, CancellationToken cancellationToken)
    {
        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceRecord? checkInRecord = null;
        AttendanceRecord? checkOutRecord = null;

        var dayRecords = request.Records.Where(r => r.CheckTime.Date == request.Date.Date).ToList();

        if (dayRecords.Any())
        {
            var first = dayRecords.First();
            checkIn = first.CheckTime;
            checkInRecord = first;

            if (dayRecords.Count > 1)
            {
                var last = dayRecords.Last();
                checkOut = last.CheckTime;
                checkOutRecord = last;
            }
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
            null,
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
