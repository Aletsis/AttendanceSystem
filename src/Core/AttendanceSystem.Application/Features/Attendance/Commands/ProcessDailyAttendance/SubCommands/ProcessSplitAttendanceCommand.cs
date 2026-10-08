using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessSplitAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    bool IsAutoDetectedShift = false) : IRequest;

public class ProcessSplitAttendanceCommandHandler : IRequestHandler<ProcessSplitAttendanceCommand>
{
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
            _logger.LogError("El turno {ShiftName} no tiene configurado el segundo bloque.", request.Shift.Name);
            return;
        }

        var schedB1In = request.Date.Date.Add(request.Shift.StartTime);
        var schedB1Out = request.Date.Date.Add(request.Shift.EndTime);
        var schedB2In = request.Date.Date.Add(request.Shift.SecondBlockStartTime.Value);
        var schedB2Out = request.Shift.SecondBlockEndTime.Value <= request.Shift.SecondBlockStartTime.Value
            ? request.Date.Date.AddDays(1).Add(request.Shift.SecondBlockEndTime.Value)
            : request.Date.Date.Add(request.Shift.SecondBlockEndTime.Value);

        // Punto medio entre la salida del bloque 1 y la entrada del bloque 2
        var midpoint = schedB1Out.AddTicks((schedB2In - schedB1Out).Ticks / 2);

        // Separar registros pendientes en bloque 1 y bloque 2
        var pendingRecords = request.Records
            .Where(r => r.Status == AttendanceStatus.Pending)
            .OrderBy(r => r.CheckTime)
            .ToList();

        var b1Records = pendingRecords
            .Where(r => r.CheckTime < midpoint)
            .ToList();

        // Límite superior para el Bloque 2: permite salidas con horas extra en la madrugada del día siguiente
        // sin invadir la ventana de entrada de la jornada del día siguiente.
        var tomorrowB1In = request.Date.Date.AddDays(1).Add(request.Shift.StartTime);
        var maxB2CutoffTomorrow = tomorrowB1In.AddHours(-2.5);
        var maxB2EndBySched = schedB2Out.AddHours(7);
        var b2Cutoff = maxB2CutoffTomorrow > schedB2Out
            ? (maxB2EndBySched < maxB2CutoffTomorrow ? maxB2EndBySched : maxB2CutoffTomorrow)
            : maxB2EndBySched;

        var b2Records = pendingRecords
            .Where(r => r.CheckTime >= midpoint && r.CheckTime <= b2Cutoff)
            .ToList();

        AttendanceRecord? b1InRecord = null;
        AttendanceRecord? b1OutRecord = null;
        AttendanceRecord? b2InRecord = null;
        AttendanceRecord? b2OutRecord = null;

        // --- Procesamiento Bloque 1 ---
        if (b1Records.Count >= 2)
        {
            b1InRecord = b1Records.First();
            b1OutRecord = b1Records.Last();

            var middleB1 = b1Records.Skip(1).Take(b1Records.Count - 2).ToList();
            foreach (var mid in middleB1)
            {
                mid.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(mid, cancellationToken);
            }
        }
        else if (b1Records.Count == 1)
        {
            var single = b1Records[0];
            var diffIn = Math.Abs((single.CheckTime - schedB1In).TotalMinutes);
            var diffOut = Math.Abs((single.CheckTime - schedB1Out).TotalMinutes);

            if (diffIn <= diffOut)
            {
                b1InRecord = single;
            }
            else
            {
                b1OutRecord = single;
            }
        }

        // --- Procesamiento Bloque 2 ---
        if (b2Records.Count >= 2)
        {
            b2InRecord = b2Records.First();
            b2OutRecord = b2Records.Last();

            var middleB2 = b2Records.Skip(1).Take(b2Records.Count - 2).ToList();
            foreach (var mid in middleB2)
            {
                mid.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(mid, cancellationToken);
            }
        }
        else if (b2Records.Count == 1)
        {
            var single = b2Records[0];
            var diffIn = Math.Abs((single.CheckTime - schedB2In).TotalMinutes);
            var diffOut = Math.Abs((single.CheckTime - schedB2Out).TotalMinutes);

            if (diffIn <= diffOut)
            {
                b2InRecord = single;
            }
            else
            {
                b2OutRecord = single;
            }
        }

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
            request.IsAutoDetectedShift);

        if (b1OutRecord != null)
        {
            dailyAttendance.SetBlock1CheckOut(b1OutRecord.CheckTime, b1OutRecord.Id);
        }

        if (b2InRecord != null)
        {
            dailyAttendance.SetBlock2CheckIn(b2InRecord.CheckTime, b2InRecord.Id);
        }

        _dailyRepo.Add(dailyAttendance);
    }
}
