using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessFlexibleAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    bool IsAutoDetectedShift = false) : IRequest;

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
        if (request.Shift.PunchTrackingMode == PunchTrackingMode.MultiInterval)
        {
            var pendingRecords = request.Records
                .Where(r => r.Status == AttendanceStatus.Pending &&
                           (r.CheckTime.Date == request.Date.Date || (r.CheckTime - request.Date).TotalHours <= 24))
                .OrderBy(r => r.CheckTime)
                .ToList();

            // Filtrar dobles toques inmediatos (< 15 segundos)
            var cleanRecords = new List<AttendanceRecord>();
            foreach (var r in pendingRecords)
            {
                if (!cleanRecords.Any() || (r.CheckTime - cleanRecords.Last().CheckTime).TotalSeconds >= 15)
                {
                    cleanRecords.Add(r);
                }
            }

            var intervals = new List<AttendanceIntervalDto>();
            int totalWorked = 0;
            AttendanceRecord? firstIn = cleanRecords.FirstOrDefault();
            AttendanceRecord? lastOut = null;

            for (int i = 0; i < cleanRecords.Count; i += 2)
            {
                var inRec = cleanRecords[i];
                inRec.MarkAsProcessed();
                inRec.SetInferredType(CheckType.CheckIn);
                await _attendanceRepo.UpdateAsync(inRec, cancellationToken);

                if (i + 1 < cleanRecords.Count)
                {
                    var outRec = cleanRecords[i + 1];
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
                intervalsData: intervalsJson);

            _dailyRepo.Add(multiDailyAttendance);
            return;
        }

        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceRecord? checkInRecord = null;
        AttendanceRecord? checkOutRecord = null;
        (int Lunch, bool HasTempExits, int TempMinutes)? intermediateAnalysis = null;

        // Marcajes del día (o dentro de las siguientes 24 horas para turnos que puedan cruzar la noche)
        var pendingDayRecords = request.Records
            .Where(r => r.Status == AttendanceStatus.Pending && r.CheckTime.Date == request.Date.Date)
            .OrderBy(r => r.CheckTime)
            .ToList();

        if (pendingDayRecords.Any())
        {
            // 1. Primer registro = Entrada oficial
            checkInRecord = pendingDayRecords.First();
            checkIn = checkInRecord.CheckTime;

            // 2. Buscar salida: el último marcaje del día o hasta 24h tras la entrada
            var exitCandidates = request.Records
                .Where(r => r.CheckTime > checkIn.Value && (r.CheckTime - checkIn.Value).TotalHours <= 24)
                .OrderBy(r => r.CheckTime)
                .ToList();

            if (exitCandidates.Any())
            {
                checkOutRecord = exitCandidates.Last();
                checkOut = checkOutRecord.CheckTime;

                // 3. Registros intermedios entre entrada y salida
                var middleRecords = exitCandidates
                    .Take(exitCandidates.Count - 1)
                    .Where(r => r.Id != checkInRecord.Id && r.Id != checkOutRecord.Id)
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

                    var recordsToAnalyze = middleRecords
                        .Except(entryDoubleTaps)
                        .Except(exitDoubleTaps)
                        .OrderBy(r => r.CheckTime)
                        .ToList();

                    int lunchMinutesDeducted = 0;
                    bool hasTemporaryExits = false;
                    int temporaryExitMinutes = 0;

                    for (int i = 0; i < recordsToAnalyze.Count; i += 2)
                    {
                        var exitRecord = recordsToAnalyze[i];

                        if (i + 1 < recordsToAnalyze.Count)
                        {
                            var returnRecord = recordsToAnalyze[i + 1];
                            double absenceMinutes = (returnRecord.CheckTime - exitRecord.CheckTime).TotalMinutes;

                            if (absenceMinutes <= 15)
                            {
                                _logger.LogDebug("Par intermedio ({Exit}-{Return}): {Min} min -> doble toque intermedio, ignorado.",
                                    exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes);
                            }
                            else if (absenceMinutes <= 90)
                            {
                                hasTemporaryExits = true;
                                temporaryExitMinutes += (int)absenceMinutes;
                                _logger.LogInformation("Par intermedio ({Exit}-{Return}): {Min} min -> posible permiso temporal detectado.",
                                    exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes);
                            }
                            else
                            {
                                if (request.Shift.LunchBreakMinutes > 0)
                                {
                                    lunchMinutesDeducted += request.Shift.LunchBreakMinutes;
                                    _logger.LogDebug("Par intermedio ({Exit}-{Return}): {Min} min -> comida formal. Deduciendo {Lunch} min.",
                                        exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes, request.Shift.LunchBreakMinutes);
                                }
                            }
                        }
                        else
                        {
                            hasTemporaryExits = true;
                            _logger.LogWarning("Checada intermedia huérfana ({Exit}) sin regreso registrado en turno flexible.",
                                exitRecord.CheckTime);
                        }
                    }

                    intermediateAnalysis = (lunchMinutesDeducted, hasTemporaryExits, temporaryExitMinutes);

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
            overtimeCalculationMethod: request.Employee.OvertimeCalculationMethod);

        if (intermediateAnalysis.HasValue)
        {
            dailyAttendance.ApplyIntermediateAnalysis(
                intermediateAnalysis.Value.Lunch,
                intermediateAnalysis.Value.HasTempExits,
                intermediateAnalysis.Value.TempMinutes);
        }

        _dailyRepo.Add(dailyAttendance);
    }
}

public record AttendanceIntervalDto(
    DateTime CheckIn,
    DateTime CheckOut,
    DateTime ReferenceCheckIn,
    DateTime ReferenceCheckOut,
    int WorkedMinutes);

