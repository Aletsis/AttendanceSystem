using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;

public sealed record ManuallyAssignAttendanceCommand(
    string EmployeeId,
    DateOnly Date,
    string RecordId,
    string AssignmentType) : IRequest<Result>;

public sealed class ManuallyAssignAttendanceCommandHandler : IRequestHandler<ManuallyAssignAttendanceCommand, Result>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepo;
    private readonly IPunchAssignmentRepository _assignmentRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceCalculationLockService _lockService;
    private readonly IPersistenceFailureClassifier _failureClassifier;

    public ManuallyAssignAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IAttendanceAdjustmentRepository adjustmentRepo,
        IPunchAssignmentRepository assignmentRepo,
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        IPersistenceFailureClassifier? failureClassifier = null)
    {
        _lockService = lockService;
        _failureClassifier = failureClassifier ?? DefaultPersistenceFailureClassifier.Instance;
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _adjustmentRepo = adjustmentRepo;
        _assignmentRepo = assignmentRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ManuallyAssignAttendanceCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);

        // F03-03: mismo protocolo que el recálculo. Con el bloqueo del empleado, un recálculo concurrente
        // espera y luego lee esta decisión (o esta asignación espera y actúa sobre el resultado nuevo).
        try
        {
            return await ExclusiveAttendanceWrite.RunAsync(
                _unitOfWork,
                _lockService,
                new[] { employeeId },
                () => AssignAsync(request, employeeId, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (_failureClassifier.Classify(ex).Kind == PersistenceFailureKind.ConcurrencyConflict)
        {
            return Result.Failure("La jornada cambió mientras se editaba (conflicto de concurrencia). Recargue e intente de nuevo.");
        }
        catch (Exception ex) when (_failureClassifier.Classify(ex).Kind == PersistenceFailureKind.LockTimeout)
        {
            return Result.Failure("La jornada está siendo recalculada por otro proceso. Intente de nuevo en unos segundos.");
        }
    }

    private async Task<Result> AssignAsync(ManuallyAssignAttendanceCommand request, EmployeeId employeeId, CancellationToken cancellationToken)
    {
        var recordId = AttendanceRecordId.From(request.RecordId);

        // 1. Logica de asignación inteligente para turnos que cruzan la medianoche
        DateOnly targetDate = request.Date;

        // Si se está asignando una salida, verificar si pertenece al día anterior
        if (request.AssignmentType == "Salida")
        {
            var yesterday = request.Date.AddDays(-1);
            var yesterdayDA = await _dailyRepo.GetByEmployeeAndDateAsync(
                employeeId,
                yesterday.ToDateTime(TimeOnly.MinValue),
                cancellationToken);

            // Si ayer tiene un turno que cruza la medianoche y este registro es en la mañana, podría pertenecer a ayer.
            // O si ayer tiene una entrada y no salida, mientras que hoy apenas comienza
            if (yesterdayDA != null && yesterdayDA.ActualCheckIn.HasValue)
            {
                var attendanceRecord = await _attendanceRepo.GetByIdAsync(recordId, cancellationToken);
                if (attendanceRecord != null)
                {
                    // Si el registro es antes de la entrada programada de hoy (o temprano en la mañana)
                    // y ayer fue un turno nocturno, casi con certeza pertenece a ayer.
                    bool belongsToYesterday = false;

                    if (yesterdayDA.ShiftType == ShiftType.Continuo ||
                        (yesterdayDA.ScheduledCheckOut.HasValue && yesterdayDA.ScheduledCheckOut < yesterdayDA.ScheduledCheckIn))
                    {
                        // Está dentro de las 16 horas posteriores a la entrada de ayer, es un candidato.
                        var diffHours = (attendanceRecord.CheckTime - yesterdayDA.ActualCheckIn.Value).TotalHours;
                        if (diffHours > 0 && diffHours < 18)
                        {
                            belongsToYesterday = true;
                        }
                    }

                    if (belongsToYesterday)
                    {
                        targetDate = yesterday;
                    }
                }
            }
        }

        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId,
            targetDate.ToDateTime(TimeOnly.MinValue),
            cancellationToken);

        if (daily == null)
        {
            return Result.Failure($"Debe procesar la asistencia del día {targetDate:dd/MM/yyyy} antes de realizar asignaciones manuales.");
        }

        // 2. Obtenemos el registro específico
        var record = await _attendanceRepo.GetByIdAsync(recordId, cancellationToken);
        if (record == null)
        {
            return Result.Failure("Registro de asistencia no encontrado.");
        }

        // F02-02 / H07: validar antes de cualquier escritura para no dejar cambios parciales.
        if (record.EmployeeId != employeeId)
        {
            return Result.Failure("El marcaje no pertenece al empleado indicado.");
        }

        var workDate = DateOnly.FromDateTime(daily.Date);
        if (request.AssignmentType != "None")
        {
            var owner = await FindOtherOwnerAsync(record, daily, workDate, cancellationToken);
            if (owner.HasValue)
            {
                return Result.Failure($"El marcaje ya está asignado a la jornada del {owner.Value:dd/MM/yyyy}. Desasígnelo primero.");
            }

            var recordWorkDate = record.GetWorkDate(daily.TimeZoneId);
            if (recordWorkDate < workDate.AddDays(-PunchAssignmentPolicy.MaxManualDaysBeforeWorkDate) ||
                recordWorkDate > workDate.AddDays(PunchAssignmentPolicy.MaxDaysAfterWorkDate))
            {
                return Result.Failure($"El marcaje del {recordWorkDate:dd/MM/yyyy} es incompatible con la jornada del {workDate:dd/MM/yyyy}.");
            }
        }

        var previousPositions = new[] { daily.CheckInRecordId, daily.Block1CheckOutRecordId, daily.Block2CheckInRecordId, daily.CheckOutRecordId }
            .Where(id => id != null)
            .Select(id => id!)
            .ToList();

        // 3. Update Logic y persistencia de ajuste manual
        var adjustment = await _adjustmentRepo.GetByEmployeeAndDateAsync(employeeId, daily.Date, cancellationToken);
        if (adjustment == null)
        {
            adjustment = AttendanceAdjustment.Create(employeeId, daily.Date, "Usuario RH");
            await _adjustmentRepo.AddAsync(adjustment, cancellationToken);
        }
        else
        {
            _adjustmentRepo.Update(adjustment);
        }

        // 3. Actualizar el DailyAttendance según el tipo de asignación
        if (request.AssignmentType == "Entrada" || request.AssignmentType == "Entrada B1") // CheckIn
        {
            if (daily.CheckOutRecordId == recordId) daily.RemoveCheckOut();
            if (daily.Block1CheckOutRecordId == recordId) daily.RemoveBlock1CheckOut();
            if (daily.Block2CheckInRecordId == recordId) daily.RemoveBlock2CheckIn();

            daily.SetCheckIn(record.CheckTime, record.Id);
            adjustment.SetCheckIn(record.Id, record.CheckTime, "Usuario RH", "Asignación manual de entrada");

            if (record.Status != AttendanceStatus.Processed)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        else if (request.AssignmentType == "Salida B1")
        {
            if (daily.CheckInRecordId == recordId) daily.RemoveCheckIn();
            if (daily.CheckOutRecordId == recordId) daily.RemoveCheckOut();
            if (daily.Block2CheckInRecordId == recordId) daily.RemoveBlock2CheckIn();

            daily.SetBlock1CheckOut(record.CheckTime, record.Id);
            adjustment.SetBlock1CheckOut(record.Id, record.CheckTime, "Usuario RH", "Asignación manual de salida B1");

            if (record.Status != AttendanceStatus.Processed)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        else if (request.AssignmentType == "Entrada B2")
        {
            if (daily.CheckInRecordId == recordId) daily.RemoveCheckIn();
            if (daily.CheckOutRecordId == recordId) daily.RemoveCheckOut();
            if (daily.Block1CheckOutRecordId == recordId) daily.RemoveBlock1CheckOut();

            daily.SetBlock2CheckIn(record.CheckTime, record.Id);
            adjustment.SetBlock2CheckIn(record.Id, record.CheckTime, "Usuario RH", "Asignación manual de entrada B2");

            if (record.Status != AttendanceStatus.Processed)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        else if (request.AssignmentType == "Salida" || request.AssignmentType == "Salida B2") // CheckOut
        {
            if (daily.CheckInRecordId == recordId) daily.RemoveCheckIn();
            if (daily.Block1CheckOutRecordId == recordId) daily.RemoveBlock1CheckOut();
            if (daily.Block2CheckInRecordId == recordId) daily.RemoveBlock2CheckIn();

            daily.SetCheckOut(record.CheckTime, record.Id);
            adjustment.SetCheckOut(record.Id, record.CheckTime, "Usuario RH", "Asignación manual de salida");

            if (record.Status != AttendanceStatus.Processed)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        else if (request.AssignmentType == "None") // Unassign
        {
            if (daily.CheckInRecordId == recordId) { daily.RemoveCheckIn(); adjustment.RemoveCheckIn("Usuario RH"); }
            if (daily.CheckOutRecordId == recordId) { daily.RemoveCheckOut(); adjustment.RemoveCheckOut("Usuario RH"); }
            if (daily.Block1CheckOutRecordId == recordId) { daily.RemoveBlock1CheckOut(); adjustment.RemoveBlock1CheckOut("Usuario RH"); }
            if (daily.Block2CheckInRecordId == recordId) { daily.RemoveBlock2CheckIn(); adjustment.RemoveBlock2CheckIn("Usuario RH"); }

            record.ResetStatus();
            await _attendanceRepo.UpdateAsync(record, cancellationToken);
        }
        else
        {
            return Result.Failure("Tipo de asignación inválido. Use 'Entrada', 'Salida B1', 'Entrada B2' o 'Salida'.");
        }

        await SynchronizePositionsAsync(daily, workDate, record, request.AssignmentType != "None", previousPositions, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Jornada distinta que ya consume el marcaje: fila de asignación o, para datos heredados,
    /// referencia en una jornada vecina.
    /// </summary>
    private async Task<DateOnly?> FindOtherOwnerAsync(AttendanceRecord record, DailyAttendance daily, DateOnly workDate, CancellationToken cancellationToken)
    {
        var assignment = await _assignmentRepo.GetByRecordIdAsync(record.Id, cancellationToken);
        if (assignment != null)
        {
            return assignment.WorkDate != workDate ? assignment.WorkDate : null;
        }

        foreach (var neighbor in new[] { workDate.AddDays(-1), workDate.AddDays(1) })
        {
            var other = await _dailyRepo.GetByEmployeeAndDateAsync(daily.EmployeeId, neighbor.ToDateTime(TimeOnly.MinValue), cancellationToken);
            if (other == null || other.Id == daily.Id) continue;
            if (other.CheckInRecordId == record.Id || other.CheckOutRecordId == record.Id ||
                other.Block1CheckOutRecordId == record.Id || other.Block2CheckInRecordId == record.Id)
            {
                return neighbor;
            }
        }

        return null;
    }

    /// <summary>
    /// Alinea las filas de posición de la jornada con su estado final. El marcaje asignado queda como
    /// decisión manual; los desplazados de su posición vuelven a ser candidatos libres.
    /// </summary>
    private async Task SynchronizePositionsAsync(
        DailyAttendance daily,
        DateOnly workDate,
        AttendanceRecord manualRecord,
        bool isAssignment,
        IReadOnlyCollection<AttendanceRecordId> previousPositions,
        CancellationToken cancellationToken)
    {
        var desired = new Dictionary<AttendanceRecordId, PunchAssignmentRole>();
        if (daily.CheckInRecordId != null) desired[daily.CheckInRecordId] = PunchAssignmentRole.CheckIn;
        if (daily.Block1CheckOutRecordId != null) desired[daily.Block1CheckOutRecordId] = PunchAssignmentRole.Block1CheckOut;
        if (daily.Block2CheckInRecordId != null) desired[daily.Block2CheckInRecordId] = PunchAssignmentRole.Block2CheckIn;
        if (daily.CheckOutRecordId != null) desired[daily.CheckOutRecordId] = PunchAssignmentRole.CheckOut;

        var stored = (await _assignmentRepo.GetByEmployeeAndWorkDateRangeAsync(daily.EmployeeId, workDate, workDate, cancellationToken))
            ?? new List<PunchAssignment>();

        foreach (var assignment in stored.Where(a => a.IsPosition && !desired.ContainsKey(a.AttendanceRecordId)))
        {
            _assignmentRepo.Remove(assignment);
        }

        // Marcajes que dejaron su posición (por fila o, en datos heredados, por columna previa) vuelven a ser libres.
        var displacedIds = stored.Where(a => a.IsPosition).Select(a => a.AttendanceRecordId)
            .Concat(previousPositions)
            .Where(id => !desired.ContainsKey(id) && id != manualRecord.Id)
            .Distinct()
            .ToList();
        foreach (var displacedId in displacedIds)
        {
            var displaced = await _attendanceRepo.GetByIdAsync(displacedId, cancellationToken);
            if (displaced != null && displaced.Status != AttendanceStatus.Pending)
            {
                displaced.ResetStatus();
                await _attendanceRepo.UpdateAsync(displaced, cancellationToken);
            }
        }

        if (!isAssignment)
        {
            var secondary = stored.FirstOrDefault(a => !a.IsPosition && a.AttendanceRecordId == manualRecord.Id);
            if (secondary != null) _assignmentRepo.Remove(secondary);
        }

        var storedById = stored.ToDictionary(a => a.AttendanceRecordId);
        foreach (var (recordId, role) in desired)
        {
            var isManual = isAssignment && recordId == manualRecord.Id;
            if (storedById.TryGetValue(recordId, out var existing))
            {
                var origin = isManual ? PunchAssignmentOrigin.Manual : existing.Origin;
                if (existing.Role != role || existing.Origin != origin)
                {
                    existing.Reassign(workDate, role, origin);
                    _assignmentRepo.Update(existing);
                }
            }
            else
            {
                // Referencia heredada a un marcaje que otra jornada ya reclama: la reporta el diagnóstico F02-07.
                if (!isManual && await _assignmentRepo.GetByRecordIdAsync(recordId, cancellationToken) != null) continue;

                _assignmentRepo.Add(PunchAssignment.Create(
                    recordId,
                    daily.EmployeeId,
                    workDate,
                    role,
                    isManual ? PunchAssignmentOrigin.Manual : PunchAssignmentOrigin.Automatic));
            }
        }
    }
}
