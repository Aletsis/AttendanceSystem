using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Enumerations;

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
    private readonly IUnitOfWork _unitOfWork;

    public ManuallyAssignAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IAttendanceAdjustmentRepository adjustmentRepo,
        IUnitOfWork unitOfWork)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _adjustmentRepo = adjustmentRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ManuallyAssignAttendanceCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
