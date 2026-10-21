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

public record ProcessFlexibleAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    bool IsAutoDetectedShift = false,
    AttendanceAdjustment? Adjustment = null,
    string? TimeZoneId = null,
    PunchAssignmentDraft? Draft = null,
    DateTime? NextJornadaEntryStart = null,
    bool Continuous = false,
    LunchPolicy? Lunch = null) : IRequest;

public class ProcessFlexibleAttendanceCommandHandler : IRequestHandler<ProcessFlexibleAttendanceCommand>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILogger<ProcessFlexibleAttendanceCommandHandler> _logger;

    public ProcessFlexibleAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        ILogger<ProcessFlexibleAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _logger = logger;
    }

    public async Task Handle(ProcessFlexibleAttendanceCommand request, CancellationToken cancellationToken)
    {
        var targetDateOnly = DateOnly.FromDateTime(request.Date);

        if (request.Shift.PunchTrackingMode == PunchTrackingMode.MultiInterval)
        {
            var pendingRecords = PunchAssignmentPolicy.Chronological(request.Records
                .Where(r => r.Status == AttendanceStatus.Pending && r.GetWorkDate(request.TimeZoneId) == targetDateOnly))
                .ToList();

            // Filtrar dobles toques inmediatos (< 15 segundos)
            var cleanRecords = new List<AttendanceRecord>();
            foreach (var r in pendingRecords)
            {
                if (!cleanRecords.Any() || (r.CheckTime - cleanRecords.Last().CheckTime).TotalSeconds >= 15)
                {
                    cleanRecords.Add(r);
                }
                else
                {
                    // D-02: el doble toque se conserva auditado como descartado, no como candidato libre.
                    r.MarkAsProcessed();
                    await _attendanceRepo.UpdateAsync(r, cancellationToken);
                    request.Draft?.DoubleTap(r);
                }
            }

            var intervals = new List<AttendanceIntervalDto>();
            int totalWorked = 0;
            AttendanceRecord? firstIn = cleanRecords.FirstOrDefault();
            AttendanceRecord? lastOut = null;
            AttendanceRecord? openInterval = null;

            for (int i = 0; i < cleanRecords.Count; i += 2)
            {
                var inRec = cleanRecords[i];
                request.Draft?.IntervalBoundary(inRec);
                inRec.MarkAsProcessed();
                inRec.SetInferredType(CheckType.CheckIn);
                await _attendanceRepo.UpdateAsync(inRec, cancellationToken);

                if (i + 1 < cleanRecords.Count)
                {
                    var outRec = cleanRecords[i + 1];
                    request.Draft?.IntervalBoundary(outRec);
                    outRec.MarkAsProcessed();
                    outRec.SetInferredType(CheckType.CheckOut);
                    await _attendanceRepo.UpdateAsync(outRec, cancellationToken);
                    lastOut = outRec;

                    DateTime refIn = inRec.CheckTime;
                    if (request.Shift.RoundingsEnabled && request.Shift.RoundingInterval > 0)
                    {
                        refIn = DailyAttendance.RoundEntry(inRec.CheckTime, request.Shift.RoundingInterval, request.Shift.ToleranceMinutes);
                    }
                    DateTime refOut = outRec.CheckTime;

                    int intervalMinutes = (int)Math.Max(0, (refOut - refIn).TotalMinutes);
                    totalWorked += intervalMinutes;

                    intervals.Add(new AttendanceIntervalDto(inRec.CheckTime, outRec.CheckTime, refIn, refOut, intervalMinutes));
                }
                else
                {
                    // F06-02 / C-07 / H09: el marcaje impar abre un intervalo sin salida. Se conserva visible y no
                    // suma minutos; sólo cuentan los intervalos cerrados.
                    openInterval = inRec;
                    intervals.Add(new AttendanceIntervalDto(inRec.CheckTime, null, inRec.CheckTime, null, 0, IsOpen: true));
                }
            }

            string? intervalsJson = intervals.Any() ? System.Text.Json.JsonSerializer.Serialize(intervals) : null;

            var multiDailyAttendance = DailyAttendance.Create(
                request.Employee.Id,
                request.Date,
                request.Shift,
                firstIn?.CheckTime,
                lastOut?.CheckTime,
                request.IsRestDay,
                firstIn?.Id,
                lastOut?.Id,
                request.Employee.CalculateOvertimeBeforeEntry,
                request.Employee.OvertimeAuthorized,
                request.IsAutoDetectedShift,
                punchTrackingMode: request.Shift.PunchTrackingMode,
                hasEntryWindow: request.Shift.HasEntryWindow,
                overtimeCalculationMethod: request.Employee.OvertimeCalculationMethod,
                totalWorkedMinutes: totalWorked,
                intervalsData: intervalsJson,
                timeZoneId: request.TimeZoneId);

            if (openInterval != null)
            {
                multiDailyAttendance.MarkOpenInterval(
                    $"Intervalo abierto sin salida registrada desde las {openInterval.GetCivilTime(request.TimeZoneId):HH:mm}.");
            }

            // F05-04: MultiInterval también aplica descanso, autorizaciones y asignaciones manuales.
            if (request.Adjustment != null)
            {
                multiDailyAttendance.ApplyAdjustment(request.Adjustment);
            }

            _dailyRepo.Add(multiDailyAttendance);
            request.Draft?.Complete(multiDailyAttendance);
            return;
        }

        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceRecord? checkInRecord = null;
        AttendanceRecord? checkOutRecord = null;
        (int LunchMinutes, int LunchExcess, bool HasTempExits, int TempMinutes)? intermediateAnalysis = null;

        // Marcajes del día
        var pendingDayRecords = PunchAssignmentPolicy.Chronological(request.Records
            .Where(r => r.Status == AttendanceStatus.Pending && r.GetWorkDate(request.TimeZoneId) == targetDateOnly))
            .ToList();

        // F02-05: salidas e intermedios sólo entre marcajes libres; nunca se toma uno consumido por otra jornada.
        var availableRecords = PunchAssignmentPolicy.Chronological(request.Records
            .Where(r => r.Status == AttendanceStatus.Pending))
            .ToList();

        if (pendingDayRecords.Any())
        {
            // 1. Primer registro = Entrada oficial
            checkInRecord = pendingDayRecords.First();
            checkIn = checkInRecord.CheckTime;

            // 2. Ventana de salida (F06-01): los marcajes del mismo día siempre son candidatos; en el día siguiente
            //    sólo hasta el fin de la ventana (entrada programada + duración + margen, antes de la jornada siguiente).
            var windowEnd = JornadaWindowPolicy.ExitWindowEnd(
                checkInRecord.GetCivilTime(request.TimeZoneId),
                JornadaWindowPolicy.ScheduledStart(request.Date, request.Shift.StartTime, request.Shift.HasEntryWindow),
                request.Shift.WorkHours,
                request.NextJornadaEntryStart);
            var exitCandidates = availableRecords
                .Where(r => r.CheckTime > checkIn.Value &&
                            (r.GetWorkDate(request.TimeZoneId) == targetDateOnly || r.GetCivilTime(request.TimeZoneId) <= windowEnd))
                .ToList();

            if (request.Continuous)
            {
                // Continuo: la salida es el último marcaje de la ventana; los anteriores son intermedios.
                checkOutRecord = exitCandidates.LastOrDefault();
            }
            else
            {
                // Flexible: la salida es el último marcaje del mismo día; si no lo hay, el primero del día
                // siguiente dentro de la ventana (jornada que cruza medianoche).
                checkOutRecord = exitCandidates.LastOrDefault(r => r.GetWorkDate(request.TimeZoneId) == targetDateOnly)
                    ?? exitCandidates.FirstOrDefault();
            }

            if (checkOutRecord != null)
            {
                checkOut = checkOutRecord.CheckTime;
            }

            if (checkOutRecord != null && checkOut.HasValue)
            {
                // 3. Registros intermedios estrictamente entre entrada y salida
                var middleRecords = availableRecords
                    .Where(r => r.CheckTime > checkIn.Value && r.CheckTime < checkOut.Value && r.Id != checkInRecord.Id && r.Id != checkOutRecord.Id)
                    .ToList();

                if (middleRecords.Any())
                {
                    const int doubleTapThresholdMinutes = 15;

                    var entryDoubleTaps = middleRecords
                        .Where(r => (r.CheckTime - checkIn.Value).TotalMinutes <= doubleTapThresholdMinutes)
                        .ToList();

                    var exitDoubleTaps = checkOut.HasValue
                        ? middleRecords
                            .Where(r => (checkOut.Value - r.CheckTime).TotalMinutes <= doubleTapThresholdMinutes)
                            .ToList()
                        : new List<AttendanceRecord>();

                    var recordsToAnalyze = PunchAssignmentPolicy.Chronological(middleRecords
                        .Except(entryDoubleTaps)
                        .Except(exitDoubleTaps))
                        .ToList();

                    foreach (var doubleTap in entryDoubleTaps.Concat(exitDoubleTaps))
                    {
                        request.Draft?.DoubleTap(doubleTap);
                    }

                    // F06-05: comida por ventana global y minutos con goce; el resto son salidas temporales.
                    intermediateAnalysis = IntermediatePunchAnalysis.Analyze(
                        recordsToAnalyze,
                        (request.Lunch ?? LunchPolicy.Default).ForShift(request.Shift.PaidLunchMinutes),
                        request.TimeZoneId,
                        request.Draft);

                    foreach (var middle in middleRecords)
                    {
                        middle.MarkAsProcessed();
                        await _attendanceRepo.UpdateAsync(middle, cancellationToken);
                    }
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
            punchTrackingMode: request.Shift.PunchTrackingMode,
            hasEntryWindow: request.Shift.HasEntryWindow,
            overtimeCalculationMethod: request.Employee.OvertimeCalculationMethod,
            timeZoneId: request.TimeZoneId);

        if (intermediateAnalysis.HasValue)
        {
            dailyAttendance.ApplyIntermediateAnalysis(
                intermediateAnalysis.Value.LunchMinutes,
                intermediateAnalysis.Value.LunchExcess,
                intermediateAnalysis.Value.HasTempExits,
                intermediateAnalysis.Value.TempMinutes);
        }

        if (request.Adjustment != null)
        {
            dailyAttendance.ApplyAdjustment(request.Adjustment);
        }

        _dailyRepo.Add(dailyAttendance);
        request.Draft?.Complete(dailyAttendance);
    }
}

/// <summary>Intervalo de una jornada MultiInterval; <c>IsOpen</c> indica un intervalo sin salida (F06-02).</summary>
public record AttendanceIntervalDto(
    DateTime CheckIn,
    DateTime? CheckOut,
    DateTime ReferenceCheckIn,
    DateTime? ReferenceCheckOut,
    int WorkedMinutes,
    bool IsOpen = false);

