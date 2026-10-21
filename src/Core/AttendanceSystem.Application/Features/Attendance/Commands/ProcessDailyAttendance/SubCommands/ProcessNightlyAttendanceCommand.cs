using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessNightlyAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    TimeSpan DayStartTime,
    TimeSpan DayEndTime,
    bool IsAutoDetectedShift = false,
    AttendanceAdjustment? Adjustment = null,
    string? TimeZoneId = null,
    PunchAssignmentDraft? Draft = null,
    LunchPolicy? Lunch = null,
    DateTime? NextJornadaEntryStart = null) : IRequest;

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

        // Entrada: la más cercana a la programada (±4 h). Salida (decisión 4b): el último marcaje desde 2 h antes
        // de la salida programada hasta 7 h después; un regreso de comida no se toma como salida.
        const double maxInDistanceMinutes = 240;  // ±4 horas
        const double exitEarlyAllowanceHours = 2;
        const double exitLateAllowanceHours = 7;

        var entryWindowStart = scheduledIn.AddHours(-4);
        var entryWindowEnd = scheduledIn.AddHours(4);

        var entryRecords = request.Records.Where(r =>
            r.GetCivilTime(request.TimeZoneId) >= entryWindowStart &&
            r.GetCivilTime(request.TimeZoneId) <= entryWindowEnd &&
            r.Status == AttendanceStatus.Pending);

        var exitWindowStart = scheduledOut.AddHours(-exitEarlyAllowanceHours);
        var exitWindowEnd = scheduledOut.AddHours(exitLateAllowanceHours);
        // Como en flexible y continuo (F06-01): no consumir la entrada de la jornada siguiente, sin recortar
        // la salida programada.
        if (request.NextJornadaEntryStart is { } nextEntry && nextEntry.AddTicks(-1) < exitWindowEnd)
        {
            exitWindowEnd = nextEntry.AddTicks(-1) > scheduledOut ? nextEntry.AddTicks(-1) : scheduledOut;
        }

        // F02-05: la salida también debe estar libre; un marcaje ya consumido por otra jornada no se reutiliza.
        var exitRecords = request.Records.Where(r =>
            r.GetCivilTime(request.TimeZoneId) >= exitWindowStart &&
            r.GetCivilTime(request.TimeZoneId) <= exitWindowEnd &&
            r.Status == AttendanceStatus.Pending);

        var matchIn = entryRecords
            .Select(r => new { Record = r, Diff = Math.Abs((r.GetCivilTime(request.TimeZoneId) - scheduledIn).TotalMinutes) })
            .Where(x => x.Diff <= maxInDistanceMinutes)
            .OrderBy(x => x.Diff)
            .ThenBy(x => x.Record.CheckTime)
            .ThenBy(x => x.Record.Id.Value)
            .FirstOrDefault();

        if (matchIn != null)
        {
            checkInRecord = matchIn.Record;
            checkIn = matchIn.Record.CheckTime;
        }

        var matchOut = exitRecords
            .Select(r => new { Record = r, Diff = Math.Abs((r.GetCivilTime(request.TimeZoneId) - scheduledOut).TotalMinutes) })
            .OrderByDescending(x => x.Record.CheckTime)
            .ThenBy(x => x.Record.Id.Value)
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
            var civilIn = checkInRecord?.GetCivilTime(request.TimeZoneId) ?? checkIn.Value;
            var civilOut = checkOutRecord?.GetCivilTime(request.TimeZoneId) ?? checkOut.Value;
            double diffIn = Math.Abs((civilIn - scheduledIn2).TotalMinutes);
            double diffOut = Math.Abs((civilOut - scheduledOut2).TotalMinutes);
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

        // Decisión 4b: marcajes entre la entrada y la salida (o el inicio de la ventana de salida si falta)
        // con la misma regla que de día y la ventana de comida nocturna.
        (int LunchMinutes, int LunchExcess, bool HasTempExits, int TempMinutes, IReadOnlyList<DateTime> Orphans)? intermediateAnalysis = null;
        string? earlyExitIssue = null;
        if (checkInRecord != null)
        {
            var upper = checkOutRecord?.CheckTime;
            var middleRecords = PunchAssignmentPolicy.Chronological(request.Records.Where(r =>
                    r.Status == AttendanceStatus.Pending &&
                    r.Id != checkInRecord.Id && r.Id != checkOutRecord?.Id &&
                    r.CheckTime > checkInRecord.CheckTime &&
                    (upper.HasValue ? r.CheckTime < upper.Value : r.GetCivilTime(request.TimeZoneId) < exitWindowStart)))
                .ToList();

            if (middleRecords.Any())
            {
                const int doubleTapThresholdMinutes = 15;
                var doubleTaps = middleRecords
                    .Where(r => (r.CheckTime - checkInRecord.CheckTime).TotalMinutes <= doubleTapThresholdMinutes ||
                                (upper.HasValue && (upper.Value - r.CheckTime).TotalMinutes <= doubleTapThresholdMinutes))
                    .ToList();
                foreach (var doubleTap in doubleTaps)
                {
                    request.Draft?.DoubleTap(doubleTap);
                }

                var recordsToAnalyze = middleRecords.Except(doubleTaps).ToList();
                intermediateAnalysis = IntermediatePunchAnalysis.Analyze(
                    recordsToAnalyze,
                    (request.Lunch ?? LunchPolicy.DefaultNight).ForShift(request.Shift.PaidLunchMinutes),
                    request.TimeZoneId,
                    request.Draft);

                if (checkOutRecord == null && recordsToAnalyze.Any())
                {
                    earlyExitIssue = $"Sin salida desde las {exitWindowStart:HH:mm}; último marcaje a las {recordsToAnalyze.Last().GetCivilTime(request.TimeZoneId):HH:mm}: posible salida anticipada, revisar.";
                }

                foreach (var middle in middleRecords)
                {
                    middle.MarkAsProcessed();
                    await _attendanceRepo.UpdateAsync(middle, cancellationToken);
                }
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
            request.Employee.OvertimeAuthorized,
            request.IsAutoDetectedShift,
            timeZoneId: request.TimeZoneId);

        if (intermediateAnalysis.HasValue)
        {
            dailyAttendance.ApplyIntermediateAnalysis(
                intermediateAnalysis.Value.LunchMinutes,
                intermediateAnalysis.Value.LunchExcess,
                intermediateAnalysis.Value.HasTempExits,
                intermediateAnalysis.Value.TempMinutes);
            foreach (var orphan in intermediateAnalysis.Value.Orphans)
            {
                dailyAttendance.AddCalculationIssue($"Marcaje intermedio sin pareja a las {orphan:HH:mm}: revisar.");
            }
        }
        if (earlyExitIssue != null)
        {
            dailyAttendance.AddCalculationIssue(earlyExitIssue);
        }

        if (request.Adjustment != null)
        {
            dailyAttendance.ApplyAdjustment(request.Adjustment);
        }

        _dailyRepo.Add(dailyAttendance);
        request.Draft?.Complete(dailyAttendance);
    }
}
