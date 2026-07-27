using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessNightlyAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    TimeSpan DayStartTime,
    TimeSpan DayEndTime) : IRequest;

public class ProcessNightlyAttendanceCommandHandler : IRequestHandler<ProcessNightlyAttendanceCommand>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILogger<ProcessNightlyAttendanceCommandHandler> _logger;

    public ProcessNightlyAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        ILogger<ProcessNightlyAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _logger = logger;
    }

    public async Task Handle(ProcessNightlyAttendanceCommand request, CancellationToken cancellationToken)
    {
        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceRecord? checkInRecord = null;
        AttendanceRecord? checkOutRecord = null;

        var scheduledIn = request.Date.Add(request.DayStartTime);
        var scheduledOut = request.Date.Add(request.DayEndTime);
        scheduledOut = scheduledOut.AddDays(1); // Cruce de día nocturno siempre asume salida al día siguiente

        // Parámetros solicitados por el usuario
        const double maxInDistanceMinutes = 240;  // ±4 horas
        const double maxOutDistanceMinutes = 420; // ±7 horas

        var entryWindowStart = scheduledIn.AddHours(-4);
        var entryWindowEnd = scheduledIn.AddHours(4);

        var entryRecords = request.Records.Where(r =>
            r.CheckTime >= entryWindowStart &&
            r.CheckTime <= entryWindowEnd &&
            r.Status == AttendanceStatus.Pending);

        var exitWindowStart = scheduledOut.AddHours(-7);
        var exitWindowEnd = scheduledOut.AddHours(7);

        var exitRecords = request.Records.Where(r =>
            r.CheckTime >= exitWindowStart &&
            r.CheckTime <= exitWindowEnd);

        var matchIn = entryRecords
            .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledIn).TotalMinutes) })
            .Where(x => x.Diff <= maxInDistanceMinutes)
            .OrderBy(x => x.Diff)
            .FirstOrDefault();

        if (matchIn != null)
        {
            checkInRecord = matchIn.Record;
            checkIn = matchIn.Record.CheckTime;
        }

        var matchOut = exitRecords
            .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledOut).TotalMinutes) })
            .Where(x => x.Diff <= maxOutDistanceMinutes)
            .OrderBy(x => x.Diff)
            .FirstOrDefault();

        if (matchOut != null)
        {
            if (checkInRecord != null && matchOut.Record.Id == checkInRecord.Id)
            {
                if (matchOut.Diff < matchIn!.Diff)
                {
                    checkOutRecord = matchOut.Record;
                    checkOut = matchOut.Record.CheckTime;
                    checkIn = null;
                    checkInRecord = null;
                }
            }
            else
            {
                checkOutRecord = matchOut.Record;
                checkOut = matchOut.Record.CheckTime;
            }
        }

        // Sanity check: salida después de entrada
        if (checkIn.HasValue && checkOut.HasValue && checkOut.Value <= checkIn.Value)
        {
            var scheduledIn2 = request.Date.Add(request.DayStartTime);
            var scheduledOut2 = request.Date.Add(request.DayEndTime).AddDays(1);
            double diffIn = Math.Abs((checkIn.Value - scheduledIn2).TotalMinutes);
            double diffOut = Math.Abs((checkOut.Value - scheduledOut2).TotalMinutes);
            if (diffIn <= diffOut)
            {
                checkOut = null;
                checkOutRecord = null;
            }
            else
            {
                checkIn = null;
                checkInRecord = null;
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
