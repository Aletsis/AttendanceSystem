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

public record ProcessRegularAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    bool IsAutoDetectedShift = false,
    AttendanceAdjustment? Adjustment = null,
    string? TimeZoneId = null,
    PunchAssignmentDraft? Draft = null,
    LunchPolicy? Lunch = null) : IRequest;

public class ProcessRegularAttendanceCommandHandler : IRequestHandler<ProcessRegularAttendanceCommand>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILogger<ProcessRegularAttendanceCommandHandler> _logger;

    public ProcessRegularAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        ILogger<ProcessRegularAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _logger = logger;
    }

    public async Task Handle(ProcessRegularAttendanceCommand request, CancellationToken cancellationToken)
    {
        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceRecord? checkInRecord = null;
        AttendanceRecord? checkOutRecord = null;
        (int LunchMinutes, int LunchExcess, bool HasTempExits, int TempMinutes, IReadOnlyList<DateTime> Orphans)? intermediateAnalysis = null;

        var targetDateOnly = DateOnly.FromDateTime(request.Date);
        var pendingDayRecords = PunchAssignmentPolicy.Chronological(request.Records
            .Where(r => r.Status == AttendanceStatus.Pending && r.GetWorkDate(request.TimeZoneId) == targetDateOnly))
            .ToList();

        if (pendingDayRecords.Count >= 1)
        {
            // PASO 1: Primer registro = Entrada oficial, Último = Salida oficial
            checkInRecord = pendingDayRecords.First();
            checkIn = checkInRecord.CheckTime;

            if (pendingDayRecords.Count >= 2)
            {
                checkOutRecord = pendingDayRecords.Last();
                checkOut = checkOutRecord.CheckTime;
            }

            // PASO 2: Registros intermedios (entre entrada y salida)
            var middleRecords = pendingDayRecords.Count >= 3
                ? pendingDayRecords.Skip(1).SkipLast(1).ToList()
                : new List<AttendanceRecord>();

            if (middleRecords.Any())
            {
                // PASO 2A: Pre-filtro — descartar dobles toques por proximidad a los extremos
                const int doubleTapThresholdMinutes = 15;

                var entryDoubleTaps = middleRecords
                    .Where(r => (r.CheckTime - checkIn!.Value).TotalMinutes <= doubleTapThresholdMinutes)
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

                _logger.LogDebug(
                    "Empleado {EmpId} - {Date}: {Total} intermedios. DoubleTap entrada={DTIn}, salida={DTOut}. Para analizar={ToAnalyze}",
                    request.Employee.Id.Value, request.Date.ToString("dd/MM/yyyy"),
                    middleRecords.Count, entryDoubleTaps.Count, exitDoubleTaps.Count, recordsToAnalyze.Count);

                // PASO 2B: Análisis de pares de ausencia
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
            // F06-05 (4a): marcaje intermedio sin pareja: incidencia visible para revisión, sin descuento.
            foreach (var orphan in intermediateAnalysis.Value.Orphans)
            {
                dailyAttendance.AddCalculationIssue($"Marcaje intermedio sin pareja a las {orphan:HH:mm}: revisar.");
            }
        }

        if (request.Adjustment != null)
        {
            dailyAttendance.ApplyAdjustment(request.Adjustment);
        }

        _dailyRepo.Add(dailyAttendance);
        request.Draft?.Complete(dailyAttendance);
    }
}
