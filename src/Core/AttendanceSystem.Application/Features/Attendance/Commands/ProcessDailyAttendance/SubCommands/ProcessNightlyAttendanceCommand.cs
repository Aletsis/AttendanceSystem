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
        // La salida es del día siguiente cuando el turno cruza medianoche (22:00–06:00); un nocturno que empieza a
        // las 00:00 termina el mismo día (cierre F06-01).
        if (request.DayEndTime <= request.DayStartTime) scheduledOut = scheduledOut.AddDays(1);

        // Caso 4: ventanas contiguas. Entrada: la más cercana a la programada desde 4 h antes hasta el inicio de la
        // ventana de salida. Salida (decisión 4b): el último marcaje desde 2 h antes de la salida programada hasta 7 h
        // después. Así ningún marcaje de la jornada queda fuera de ambas ventanas.
        const double entryEarlyAllowanceHours = 4;
        const double exitEarlyAllowanceHours = 2;
        const double exitLateAllowanceHours = 7;
        var doubleTapThresholdMinutes = (request.Lunch ?? LunchPolicy.DefaultNight).DoubleTapMinutes;

        var entryWindowStart = scheduledIn.AddHours(-entryEarlyAllowanceHours);
        var exitWindowStart = scheduledOut.AddHours(-exitEarlyAllowanceHours);
        var exitWindowEnd = scheduledOut.AddHours(exitLateAllowanceHours);
        // Como en flexible y continuo (F06-01): no consumir la entrada de la jornada siguiente, sin recortar
        // la salida programada.
        if (request.NextJornadaEntryStart is { } nextEntry && nextEntry.AddTicks(-1) < exitWindowEnd)
        {
            exitWindowEnd = nextEntry.AddTicks(-1) > scheduledOut ? nextEntry.AddTicks(-1) : scheduledOut;
        }

        // F02-05: sólo marcajes libres; uno ya consumido por otra jornada no se reutiliza.
        var jornadaRecords = PunchAssignmentPolicy.Chronological(request.Records.Where(r =>
                r.Status == AttendanceStatus.Pending &&
                r.GetCivilTime(request.TimeZoneId) >= entryWindowStart &&
                r.GetCivilTime(request.TimeZoneId) <= exitWindowEnd))
            .ToList();

        // Como en turno fijo: marcajes a no más de 15 min del primero son dobles toques de un solo marcaje útil.
        if (jornadaRecords.Count >= 2 &&
            (jornadaRecords.Last().CheckTime - jornadaRecords.First().CheckTime).TotalMinutes <= doubleTapThresholdMinutes)
        {
            var kept = IsNearerToScheduledExit(jornadaRecords.First()) ? jornadaRecords.Last() : jornadaRecords.First();
            foreach (var doubleTap in jornadaRecords.Where(r => r != kept))
            {
                request.Draft?.DoubleTap(doubleTap);
                doubleTap.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(doubleTap, cancellationToken);
            }
            jornadaRecords = new List<AttendanceRecord> { kept };
        }

        if (jornadaRecords.Count == 1)
        {
            // Caso 4 (b1), como en turno fijo: un solo marcaje es la entrada o la salida según cuál de las dos
            // programadas tenga más cerca (empate: salida).
            var single = jornadaRecords[0];
            if (IsNearerToScheduledExit(single))
            {
                checkOutRecord = single;
                checkOut = single.CheckTime;
            }
            else
            {
                checkInRecord = single;
                checkIn = single.CheckTime;
            }
        }
        else if (jornadaRecords.Count >= 2)
        {
            checkInRecord = jornadaRecords
                .Where(r => r.GetCivilTime(request.TimeZoneId) < exitWindowStart)
                .OrderBy(r => Math.Abs((r.GetCivilTime(request.TimeZoneId) - scheduledIn).TotalMinutes))
                .ThenBy(r => r.CheckTime)
                .ThenBy(r => r.Id.Value)
                .FirstOrDefault();

            checkOutRecord = jornadaRecords.LastOrDefault(r => r.GetCivilTime(request.TimeZoneId) >= exitWindowStart);

            // Caso 4 (b2), como en turno fijo: sin marcaje en la ventana de salida, el último después de la entrada
            // (que no sea un doble toque de ella) es la salida y el día queda con salida anticipada.
            if (checkOutRecord == null && checkInRecord != null)
            {
                var entryTime = checkInRecord.CheckTime;
                checkOutRecord = jornadaRecords.LastOrDefault(r =>
                    (r.CheckTime - entryTime).TotalMinutes > doubleTapThresholdMinutes);
            }

            checkIn = checkInRecord?.CheckTime;
            checkOut = checkOutRecord?.CheckTime;
        }

        // Decisión 4b: marcajes entre la entrada y la salida (o el inicio de la ventana de salida si falta)
        // con la misma regla que de día y la ventana de comida nocturna.
        (IReadOnlyList<TemporaryExit> Exits, IReadOnlyList<DateTime> Orphans)? intermediateAnalysis = null;
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
                intermediateAnalysis.Value.Exits,
                (request.Lunch ?? LunchPolicy.DefaultNight).ForShift(request.Shift.PaidLunchMinutes).PaidMinutes);
            foreach (var orphan in intermediateAnalysis.Value.Orphans)
            {
                dailyAttendance.AddCalculationIssue(IntermediatePunchAnalysis.OrphanIssue(orphan));
            }
        }
        if (request.Adjustment != null)
        {
            dailyAttendance.ApplyAdjustment(request.Adjustment);
        }

        _dailyRepo.Add(dailyAttendance);
        request.Draft?.Complete(dailyAttendance);

        bool IsNearerToScheduledExit(AttendanceRecord record)
        {
            var civil = record.GetCivilTime(request.TimeZoneId);
            return Math.Abs((civil - scheduledOut).TotalMinutes) <= Math.Abs((civil - scheduledIn).TotalMinutes);
        }
    }
}
