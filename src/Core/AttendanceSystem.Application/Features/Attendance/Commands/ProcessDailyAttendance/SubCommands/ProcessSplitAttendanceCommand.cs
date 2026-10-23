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

public record ProcessSplitAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    bool IsAutoDetectedShift = false,
    AttendanceAdjustment? Adjustment = null,
    string? TimeZoneId = null,
    PunchAssignmentDraft? Draft = null,
    DateTime? EarlyEntryStart = null) : IRequest;

public class ProcessSplitAttendanceCommandHandler : IRequestHandler<ProcessSplitAttendanceCommand>
{
    private const int DoubleTapThresholdMinutes = 15;

    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILogger<ProcessSplitAttendanceCommandHandler> _logger;

    public ProcessSplitAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        ILogger<ProcessSplitAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _logger = logger;
    }

    public async Task Handle(ProcessSplitAttendanceCommand request, CancellationToken cancellationToken)
    {
        if (!request.Shift.SecondBlockStartTime.HasValue || !request.Shift.SecondBlockEndTime.HasValue)
        {
            // F05-04: antes se retornaba sin crear la jornada y se perdía el ajuste. Se registra como un solo
            // bloque con los marcajes del día y se aplica la decisión manual.
            _logger.LogError("El turno {ShiftName} no tiene configurado el segundo bloque; se calcula como un solo bloque.", request.Shift.Name);
            await HandleSingleBlockAsync(request, cancellationToken);
            return;
        }

        var schedB1In = request.Date.Date.Add(request.Shift.StartTime);
        var schedB1Out = request.Date.Date.Add(request.Shift.EndTime);
        var schedB2In = request.Date.Date.Add(request.Shift.SecondBlockStartTime.Value);
        var schedB2Out = request.Shift.SecondBlockEndTime.Value <= request.Shift.SecondBlockStartTime.Value
            ? request.Date.Date.AddDays(1).Add(request.Shift.SecondBlockEndTime.Value)
            : request.Date.Date.Add(request.Shift.SecondBlockEndTime.Value);

        // Límite superior para el Bloque 2: permite salidas con horas extra en la madrugada del día siguiente
        // sin invadir la ventana de entrada de la jornada del día siguiente.
        var tomorrowB1In = request.Date.Date.AddDays(1).Add(request.Shift.StartTime);
        var maxB2CutoffTomorrow = tomorrowB1In.AddHours(-2.5);
        var maxB2EndBySched = schedB2Out.AddHours(7);
        var b2Cutoff = maxB2CutoffTomorrow > schedB2Out
            ? (maxB2EndBySched < maxB2CutoffTomorrow ? maxB2EndBySched : maxB2CutoffTomorrow)
            : maxB2EndBySched;

        // Posiciones en orden: entrada B1, salida B1, entrada B2, salida B2.
        var slots = new[] { schedB1In, schedB1Out, schedB2In, schedB2Out };

        var punches = PunchAssignmentPolicy.Chronological(request.Records
            .Where(r => r.Status == AttendanceStatus.Pending && r.GetCivilTime(request.TimeZoneId) <= b2Cutoff))
            .ToList();
        punches = await CollapseDoubleTapsAsync(punches, slots, request, cancellationToken);

        // Caso 8 (a/b): ya no se separa por el punto medio del descanso. Con 4 marcajes o más, el primero y el último
        // son la entrada del bloque 1 y la salida del bloque 2, y el descanso es el par seguido de marcajes más
        // cercano a la salida del bloque 1 y la entrada del bloque 2. Con menos, cada marcaje va, en orden, a la
        // posición más cercana (empate: la anterior); las que sobran quedan faltantes.
        var assigned = new AttendanceRecord?[4];
        var middleB1 = new List<AttendanceRecord>();
        var middleB2 = new List<AttendanceRecord>();
        if (punches.Count >= 4)
        {
            int breakIndex = 1;
            double bestCost = double.MaxValue;
            for (int i = 1; i + 1 <= punches.Count - 2; i++)
            {
                var cost = Distance(punches[i], slots[1], request) + Distance(punches[i + 1], slots[2], request);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    breakIndex = i;
                }
            }
            assigned[0] = punches[0];
            assigned[1] = punches[breakIndex];
            assigned[2] = punches[breakIndex + 1];
            assigned[3] = punches[^1];
            middleB1.AddRange(punches.Skip(1).Take(breakIndex - 1));
            middleB2.AddRange(punches.Skip(breakIndex + 2).Take(punches.Count - breakIndex - 3));
        }
        else if (punches.Count > 0)
        {
            var best = NearestOrderedSlots(punches, slots, request);
            for (int i = 0; i < punches.Count; i++) assigned[best[i]] = punches[i];
        }

        var b1InRecord = assigned[0];
        var b1OutRecord = assigned[1];
        var b2InRecord = assigned[2];
        var b2OutRecord = assigned[3];

        // Marcar registros asignados como procesados e inferir tipo
        if (b1InRecord != null)
        {
            b1InRecord.MarkAsProcessed();
            b1InRecord.SetInferredType(CheckType.CheckIn);
            await _attendanceRepo.UpdateAsync(b1InRecord, cancellationToken);
        }

        if (b1OutRecord != null)
        {
            b1OutRecord.MarkAsProcessed();
            b1OutRecord.SetInferredType(CheckType.CheckOut);
            await _attendanceRepo.UpdateAsync(b1OutRecord, cancellationToken);
        }

        if (b2InRecord != null)
        {
            b2InRecord.MarkAsProcessed();
            b2InRecord.SetInferredType(CheckType.CheckIn);
            await _attendanceRepo.UpdateAsync(b2InRecord, cancellationToken);
        }

        if (b2OutRecord != null)
        {
            b2OutRecord.MarkAsProcessed();
            b2OutRecord.SetInferredType(CheckType.CheckOut);
            await _attendanceRepo.UpdateAsync(b2OutRecord, cancellationToken);
        }

        // Crear la entidad DailyAttendance con los marcajes principales
        var dailyAttendance = DailyAttendance.Create(
            request.Employee.Id,
            request.Date,
            request.Shift,
            b1InRecord?.CheckTime,
            b2OutRecord?.CheckTime,
            request.IsRestDay,
            b1InRecord?.Id,
            b2OutRecord?.Id,
            request.Employee.CalculateOvertimeBeforeEntry,
            request.Employee.OvertimeAuthorized,
            request.IsAutoDetectedShift,
            timeZoneId: request.TimeZoneId);

        if (b1OutRecord != null)
        {
            dailyAttendance.SetBlock1CheckOut(b1OutRecord.CheckTime, b1OutRecord.Id);
        }

        if (b2InRecord != null)
        {
            dailyAttendance.SetBlock2CheckIn(b2InRecord.CheckTime, b2InRecord.Id);
        }

        // Caso 8 (c): los marcajes a mitad de un bloque siguen la regla del turno fijo (salidas temporales, dobles
        // toques e incidencias), sin comida: el descanso entre bloques hace de comida.
        var hasTemporaryExits = false;
        var temporaryExitMinutes = 0;
        var orphans = new List<DateTime>();
        foreach (var (middle, blockIn, blockOut) in new[] { (middleB1, b1InRecord, b1OutRecord), (middleB2, b2InRecord, b2OutRecord) })
        {
            if (middle.Count == 0) continue;
            var doubleTaps = middle
                .Where(r => (blockIn != null && (r.CheckTime - blockIn.CheckTime).TotalMinutes <= DoubleTapThresholdMinutes) ||
                            (blockOut != null && (blockOut.CheckTime - r.CheckTime).TotalMinutes <= DoubleTapThresholdMinutes))
                .ToList();
            foreach (var doubleTap in doubleTaps) request.Draft?.DoubleTap(doubleTap);

            var analysis = IntermediatePunchAnalysis.Analyze(
                PunchAssignmentPolicy.Chronological(middle.Except(doubleTaps)).ToList(), LunchPolicy.None, request.TimeZoneId, request.Draft);
            hasTemporaryExits |= analysis.HasTempExits;
            temporaryExitMinutes += analysis.TempMinutes;
            orphans.AddRange(analysis.Orphans);

            foreach (var record in middle)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        if (hasTemporaryExits || orphans.Count > 0)
        {
            dailyAttendance.ApplyIntermediateAnalysis(0, 0, hasTemporaryExits, temporaryExitMinutes);
            foreach (var orphan in orphans)
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

    private static double Distance(AttendanceRecord record, DateTime slot, ProcessSplitAttendanceCommand request) =>
        Math.Abs((record.GetCivilTime(request.TimeZoneId) - slot).TotalMinutes);

    /// <summary>
    /// Asigna hasta 3 marcajes a posiciones distintas en orden cronológico con la menor distancia total a sus horas
    /// programadas; con empate gana la combinación de posiciones más tempranas (14:00 entre 13:00 y 15:00 es salida B1).
    /// </summary>
    private static int[] NearestOrderedSlots(IReadOnlyList<AttendanceRecord> punches, DateTime[] slots, ProcessSplitAttendanceCommand request)
    {
        int[]? best = null;
        double bestCost = double.MaxValue;
        foreach (var combination in Combinations(slots.Length, punches.Count))
        {
            var cost = combination.Select((slot, i) => Distance(punches[i], slots[slot], request)).Sum();
            if (cost < bestCost)
            {
                bestCost = cost;
                best = combination;
            }
        }
        return best!;
    }

    /// <summary>Combinaciones crecientes de <paramref name="k"/> posiciones entre <paramref name="n"/>, en orden lexicográfico.</summary>
    private static IEnumerable<int[]> Combinations(int n, int k, int start = 0)
    {
        if (k == 0)
        {
            yield return Array.Empty<int>();
            yield break;
        }
        for (int i = start; i <= n - k; i++)
        {
            foreach (var rest in Combinations(n, k - 1, i + 1))
            {
                yield return new[] { i }.Concat(rest).ToArray();
            }
        }
    }

    /// <summary>
    /// Como en turno fijo: marcajes seguidos a no más de 15 min son dobles toques de uno solo. Se conserva el primero, o
    /// el último si el grupo está más cerca de una salida (B1 o B2) que de una entrada.
    /// </summary>
    private async Task<List<AttendanceRecord>> CollapseDoubleTapsAsync(
        List<AttendanceRecord> punches, DateTime[] slots, ProcessSplitAttendanceCommand request, CancellationToken cancellationToken)
    {
        var result = new List<AttendanceRecord>();
        int index = 0;
        while (index < punches.Count)
        {
            var group = new List<AttendanceRecord> { punches[index] };
            while (index + group.Count < punches.Count &&
                   (punches[index + group.Count].CheckTime - group[^1].CheckTime).TotalMinutes <= DoubleTapThresholdMinutes)
            {
                group.Add(punches[index + group.Count]);
            }
            index += group.Count;

            var nearestSlot = Enumerable.Range(0, slots.Length).MinBy(i => Distance(group[0], slots[i], request));
            var kept = nearestSlot % 2 == 1 ? group[^1] : group[0];
            result.Add(kept);
            foreach (var doubleTap in group.Where(r => r != kept))
            {
                request.Draft?.DoubleTap(doubleTap);
                doubleTap.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(doubleTap, cancellationToken);
            }
        }
        return result;
    }

    private async Task HandleSingleBlockAsync(ProcessSplitAttendanceCommand request, CancellationToken cancellationToken)
    {
        var targetDateOnly = DateOnly.FromDateTime(request.Date);
        var dayRecords = PunchAssignmentPolicy.Chronological(request.Records
            .Where(r => r.Status == AttendanceStatus.Pending && JornadaDay.Contains(r, targetDateOnly, request.TimeZoneId, request.EarlyEntryStart)))
            .ToList();

        var checkIn = dayRecords.FirstOrDefault();
        var checkOut = dayRecords.Count > 1 ? dayRecords.Last() : null;
        foreach (var middle in dayRecords.Skip(1).Take(Math.Max(0, dayRecords.Count - 2)))
        {
            request.Draft?.Intermediate(middle);
            middle.MarkAsProcessed();
            await _attendanceRepo.UpdateAsync(middle, cancellationToken);
        }
        foreach (var (record, type) in new[] { (checkIn, CheckType.CheckIn), (checkOut, CheckType.CheckOut) })
        {
            if (record == null) continue;
            record.MarkAsProcessed();
            record.SetInferredType(type);
            await _attendanceRepo.UpdateAsync(record, cancellationToken);
        }

        var dailyAttendance = DailyAttendance.Create(
            request.Employee.Id,
            request.Date,
            request.Shift,
            checkIn?.CheckTime,
            checkOut?.CheckTime,
            request.IsRestDay,
            checkIn?.Id,
            checkOut?.Id,
            request.Employee.CalculateOvertimeBeforeEntry,
            request.Employee.OvertimeAuthorized,
            request.IsAutoDetectedShift,
            timeZoneId: request.TimeZoneId);

        dailyAttendance.AddCalculationIssue("Turno partido sin segundo bloque configurado: calculado como un solo bloque.");

        if (request.Adjustment != null)
        {
            dailyAttendance.ApplyAdjustment(request.Adjustment);
        }

        _dailyRepo.Add(dailyAttendance);
        request.Draft?.Complete(dailyAttendance);
    }
}
