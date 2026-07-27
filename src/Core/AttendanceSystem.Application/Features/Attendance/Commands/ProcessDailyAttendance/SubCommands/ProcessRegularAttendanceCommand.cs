using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessRegularAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay) : IRequest;

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
        (int Lunch, bool HasTempExits, int TempMinutes)? intermediateAnalysis = null;

        var pendingDayRecords = request.Records
            .Where(r => r.Status == AttendanceStatus.Pending && r.CheckTime.Date == request.Date.Date)
            .OrderBy(r => r.CheckTime)
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

                var recordsToAnalyze = middleRecords
                    .Except(entryDoubleTaps)
                    .Except(exitDoubleTaps)
                    .OrderBy(r => r.CheckTime)
                    .ToList();

                _logger.LogDebug(
                    "Empleado {EmpId} - {Date}: {Total} intermedios. DoubleTap entrada={DTIn}, salida={DTOut}. Para analizar={ToAnalyze}",
                    request.Employee.Id.Value, request.Date.ToString("dd/MM/yyyy"),
                    middleRecords.Count, entryDoubleTaps.Count, exitDoubleTaps.Count, recordsToAnalyze.Count);

                // PASO 2B: Análisis de pares de ausencia
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
                            _logger.LogDebug("Par intermedio ({Exit}-{Return}): {Min} min → doble toque intermedio, ignorado.",
                                exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes);
                        }
                        else if (absenceMinutes <= 90)
                        {
                            hasTemporaryExits = true;
                            temporaryExitMinutes += (int)absenceMinutes;
                            _logger.LogInformation("Par intermedio ({Exit}-{Return}): {Min} min → posible permiso temporal detectado.",
                                exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes);
                        }
                        else
                        {
                            if (request.Shift.LunchBreakMinutes > 0)
                            {
                                lunchMinutesDeducted += request.Shift.LunchBreakMinutes;
                                _logger.LogDebug("Par intermedio ({Exit}-{Return}): {Min} min → comida formal. Deduciendo {Lunch} min.",
                                    exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes, request.Shift.LunchBreakMinutes);
                            }
                        }
                    }
                    else
                    {
                        hasTemporaryExits = true;
                        _logger.LogWarning("Checada intermedia huérfana ({Exit}) sin regreso registrado — posible salida sin retorno.",
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
