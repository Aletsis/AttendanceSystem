using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;

/// <summary>
/// Decisión manual sobre un marcaje en una jornada.
/// <list type="bullet">
/// <item><c>Entrada</c>/<c>Entrada B1</c>, <c>Salida B1</c>, <c>Entrada B2</c>, <c>Salida</c>/<c>Salida B2</c>: asignación forzada.</item>
/// <item><c>None</c>: desasignar con vacío forzado; la posición queda vacía y el marcaje no se reasigna automáticamente.</item>
/// <item><c>Restaurar</c>: retirar la decisión sobre el marcaje; el cálculo automático vuelve a decidir.</item>
/// </list>
/// </summary>
/// <param name="ExpectedRevision">Revisión del ajuste que vio el operador; si cambió, la operación se rechaza.</param>
/// <param name="MoveFromOtherJornada">Reasignación explícita de un marcaje que hoy pertenece a otra jornada.</param>
public sealed record ManuallyAssignAttendanceCommand(
    string EmployeeId,
    DateOnly Date,
    string RecordId,
    string AssignmentType,
    int? ExpectedRevision = null,
    bool MoveFromOtherJornada = false,
    string? Reason = null) : IRequest<Result<AttendanceDecisionOutcome>>;

public sealed class ManuallyAssignAttendanceCommandHandler : IRequestHandler<ManuallyAssignAttendanceCommand, Result<AttendanceDecisionOutcome>>
{
    public const string Unassign = "None";
    public const string Restore = "Restaurar";

    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IPunchAssignmentRepository _assignmentRepo;
    private readonly AttendanceDecisionService _decisions;

    public ManuallyAssignAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IPunchAssignmentRepository assignmentRepo,
        AttendanceDecisionService decisions)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _assignmentRepo = assignmentRepo;
        _decisions = decisions;
    }

    public static AttendancePosition? PositionFor(string assignmentType) => assignmentType switch
    {
        "Entrada" or "Entrada B1" => AttendancePosition.CheckIn,
        "Salida B1" => AttendancePosition.Block1CheckOut,
        "Entrada B2" => AttendancePosition.Block2CheckIn,
        "Salida" or "Salida B2" => AttendancePosition.CheckOut,
        _ => null
    };

    public async Task<Result<AttendanceDecisionOutcome>> Handle(ManuallyAssignAttendanceCommand request, CancellationToken cancellationToken)
    {
        var position = PositionFor(request.AssignmentType);
        if (position == null && request.AssignmentType != Unassign && request.AssignmentType != Restore)
        {
            return Result<AttendanceDecisionOutcome>.Failure("Tipo de asignación inválido. Use 'Entrada', 'Salida B1', 'Entrada B2', 'Salida', 'None' o 'Restaurar'.");
        }

        var employeeId = EmployeeId.From(request.EmployeeId);
        var recordId = AttendanceRecordId.From(request.RecordId);

        // F05-03: validar antes de cualquier escritura para no dejar cambios parciales.
        var record = await _attendanceRepo.GetByIdAsync(recordId, cancellationToken);
        if (record == null)
        {
            return Result<AttendanceDecisionOutcome>.Failure("Registro de asistencia no encontrado.");
        }
        if (record.EmployeeId != employeeId)
        {
            return Result<AttendanceDecisionOutcome>.Failure("El marcaje no pertenece al empleado indicado.");
        }

        var targetDate = await ResolveTargetDateAsync(request, employeeId, record, cancellationToken);
        var date = targetDate.ToDateTime(TimeOnly.MinValue);

        return await _decisions.DecideAsync(
            employeeId,
            date,
            request.AssignmentType,
            request.Reason,
            request.ExpectedRevision,
            (context, adjustment) => DecideAsync(context, adjustment, request, position, record, targetDate, cancellationToken),
            cancellationToken);
    }

    private async Task<Result> DecideAsync(
        AttendanceDecisionContext context,
        AttendanceAdjustment adjustment,
        ManuallyAssignAttendanceCommand request,
        AttendancePosition? position,
        AttendanceRecord record,
        DateOnly workDate,
        CancellationToken cancellationToken)
    {
        var date = workDate.ToDateTime(TimeOnly.MinValue);
        var reason = request.Reason ?? $"Decisión manual: {request.AssignmentType}";
        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(record.EmployeeId, date, cancellationToken);

        if (request.AssignmentType == Restore)
        {
            return adjustment.RestoreAutomatic(record.Id, context.Author, reason)
                ? Result.Success()
                : Result.Failure("El marcaje no tiene una decisión manual en esta jornada.");
        }

        if (request.AssignmentType == Unassign)
        {
            // F05-01: desasignar = vacío forzado de las posiciones que ocupa, con el marcaje excluido.
            var occupied = AttendanceAdjustment.AllPositions
                .Where(p => PositionRecord(daily, p) == record.Id ||
                            (adjustment.OverrideKind(p) == PositionOverrideKind.Assigned && adjustment.OverrideRecord(p) == record.Id))
                .ToList();
            if (occupied.Count == 0)
            {
                return Result.Failure("El marcaje no ocupa ninguna posición de la jornada.");
            }
            foreach (var p in occupied)
            {
                adjustment.ForceEmptyPosition(p, record.Id, context.Author, reason);
            }
            return Result.Success();
        }

        // Asignación: compatibilidad temporal, uso por otra jornada y secuencia de posiciones.
        var recordWorkDate = record.GetWorkDate(record.TimeZoneId);
        if (recordWorkDate < workDate.AddDays(-PunchAssignmentPolicy.MaxManualDaysBeforeWorkDate) ||
            recordWorkDate > workDate.AddDays(PunchAssignmentPolicy.MaxDaysAfterWorkDate))
        {
            return Result.Failure($"El marcaje del {recordWorkDate:dd/MM/yyyy} es incompatible con la jornada del {workDate:dd/MM/yyyy}.");
        }

        var owner = await FindOtherOwnerAsync(record, workDate, cancellationToken);
        if (owner.HasValue)
        {
            if (!request.MoveFromOtherJornada)
            {
                return Result.Failure($"El marcaje ya está asignado a la jornada del {owner.Value:dd/MM/yyyy}. Desasígnelo primero o solicite moverlo.");
            }

            // F05-03: reasignación legítima atómica. Se retira cualquier decisión de la otra jornada sobre el
            // marcaje y ambas jornadas se recalculan con la nueva decisión, bajo el mismo bloqueo.
            var other = await context.GetAdjustmentAsync(owner.Value.ToDateTime(TimeOnly.MinValue), cancellationToken);
            other.RestoreAutomatic(record.Id, context.Author, $"Marcaje movido a la jornada del {workDate:dd/MM/yyyy}");
            context.MarkAffected(owner.Value.ToDateTime(TimeOnly.MinValue));
        }

        var sequenceError = ValidateSequence(daily, adjustment, position!.Value, record);
        if (sequenceError != null) return Result.Failure(sequenceError);

        adjustment.AssignPosition(position.Value, record.Id, record.CheckTime, context.Author, reason);
        return Result.Success();
    }

    /// <summary>
    /// F05-03: las posiciones conservan el orden entrada &lt; salida B1 &lt; entrada B2 &lt; salida con el
    /// estado que resultará (decisiones vigentes más la nueva).
    /// </summary>
    private static string? ValidateSequence(DailyAttendance? daily, AttendanceAdjustment adjustment, AttendancePosition position, AttendanceRecord record)
    {
        var times = new Dictionary<AttendancePosition, DateTime>();
        foreach (var p in AttendanceAdjustment.AllPositions)
        {
            var kind = adjustment.OverrideKind(p);
            if (kind == PositionOverrideKind.ForcedEmpty) continue;
            if (kind == PositionOverrideKind.Assigned && adjustment.OverrideTime(p) is { } decided)
            {
                times[p] = decided;
            }
            else if (PositionTime(daily, p) is { } current && PositionRecord(daily, p) != record.Id)
            {
                times[p] = current;
            }
        }

        foreach (var p in times.Where(kv => kv.Value == record.CheckTime && kv.Key != position).Select(kv => kv.Key).ToList())
        {
            times.Remove(p); // el marcaje se mueve: deja su posición anterior
        }
        times[position] = record.CheckTime;

        var ordered = AttendanceAdjustment.AllPositions.Where(times.ContainsKey).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (times[ordered[i]] <= times[ordered[i - 1]])
            {
                return $"La asignación rompe la secuencia de la jornada: {ordered[i - 1]} ({times[ordered[i - 1]]:HH:mm} UTC) debe ser anterior a {ordered[i]} ({times[ordered[i]]:HH:mm} UTC).";
            }
        }
        return null;
    }

    private static AttendanceRecordId? PositionRecord(DailyAttendance? daily, AttendancePosition position) => daily == null ? null : position switch
    {
        AttendancePosition.CheckIn => daily.CheckInRecordId,
        AttendancePosition.Block1CheckOut => daily.Block1CheckOutRecordId,
        AttendancePosition.Block2CheckIn => daily.Block2CheckInRecordId,
        _ => daily.CheckOutRecordId
    };

    private static DateTime? PositionTime(DailyAttendance? daily, AttendancePosition position) => daily == null ? null : position switch
    {
        AttendancePosition.CheckIn => daily.ActualCheckIn,
        AttendancePosition.Block1CheckOut => daily.ActualBlock1CheckOut,
        AttendancePosition.Block2CheckIn => daily.ActualBlock2CheckIn,
        _ => daily.ActualCheckOut
    };

    /// <summary>
    /// Jornada objetivo. Una salida se atribuye al día anterior cuando su jornada cruza la medianoche y el
    /// marcaje cae dentro de su duración máxima; desasignar o restaurar actúa sobre la jornada que hoy lo tiene.
    /// </summary>
    private async Task<DateOnly> ResolveTargetDateAsync(ManuallyAssignAttendanceCommand request, EmployeeId employeeId, AttendanceRecord record, CancellationToken cancellationToken)
    {
        if (request.AssignmentType is Unassign or Restore)
        {
            var row = await _assignmentRepo.GetByRecordIdAsync(record.Id, cancellationToken);
            if (row != null && row.WorkDate >= request.Date.AddDays(-1) && row.WorkDate <= request.Date) return row.WorkDate;
            return request.Date;
        }

        if (PositionFor(request.AssignmentType) != AttendancePosition.CheckOut) return request.Date;

        var yesterday = request.Date.AddDays(-1);
        var yesterdayDA = await _dailyRepo.GetByEmployeeAndDateAsync(employeeId, yesterday.ToDateTime(TimeOnly.MinValue), cancellationToken);
        if (yesterdayDA?.ActualCheckIn is { } yesterdayIn &&
            (yesterdayDA.ShiftType == ShiftType.Continuo ||
             (yesterdayDA.ScheduledCheckOut.HasValue && yesterdayDA.ScheduledCheckOut < yesterdayDA.ScheduledCheckIn)))
        {
            var diffHours = (record.CheckTime - yesterdayIn).TotalHours;
            if (diffHours > 0 && diffHours < 18) return yesterday;
        }

        return request.Date;
    }

    /// <summary>
    /// Jornada distinta que ya consume el marcaje: fila de asignación o, para datos heredados,
    /// referencia en una jornada vecina.
    /// </summary>
    private async Task<DateOnly?> FindOtherOwnerAsync(AttendanceRecord record, DateOnly workDate, CancellationToken cancellationToken)
    {
        var assignment = await _assignmentRepo.GetByRecordIdAsync(record.Id, cancellationToken);
        if (assignment != null)
        {
            return assignment.WorkDate != workDate ? assignment.WorkDate : null;
        }

        foreach (var neighbor in new[] { workDate.AddDays(-1), workDate.AddDays(1) })
        {
            var other = await _dailyRepo.GetByEmployeeAndDateAsync(record.EmployeeId, neighbor.ToDateTime(TimeOnly.MinValue), cancellationToken);
            if (other == null) continue;
            if (other.CheckInRecordId == record.Id || other.CheckOutRecordId == record.Id ||
                other.Block1CheckOutRecordId == record.Id || other.Block2CheckInRecordId == record.Id)
            {
                return neighbor;
            }
        }

        return null;
    }
}
