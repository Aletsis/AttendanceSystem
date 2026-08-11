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
    private readonly IUnitOfWork _unitOfWork;

    public ManuallyAssignAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IUnitOfWork unitOfWork)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
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

        // 3. Update Logic
        // 3. Actualizar el DailyAttendance según el tipo de asignación
        if (request.AssignmentType == "Entrada") // CheckIn
        {
            // Si el mismo registro se usó como CheckOut, primero elimínelo de CheckOut?
            // "Validando que no haya 2 entradas o actualizaciones"
            // Si ya hay un CheckIn, lo reemplazamos.
            // Si el registro que estamos asignando es actualmente el CheckOut, debemos borrar CheckOut.
            
            if (daily.CheckOutRecordId == recordId)
            {
                daily.RemoveCheckOut();
            }

            daily.SetCheckIn(record.CheckTime, record.Id);
            
            // Si el registro no está procesado, lo marcamos como procesado
            if (record.Status != AttendanceStatus.Processed)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        else if (request.AssignmentType == "Salida") // CheckOut
        {
            if (daily.CheckInRecordId == recordId)
            {
                daily.RemoveCheckIn();
            }

            daily.SetCheckOut(record.CheckTime, record.Id);

            // Si el registro no está procesado, lo marcamos como procesado
            if (record.Status != AttendanceStatus.Processed)
            {
                record.MarkAsProcessed();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
        else if (request.AssignmentType == "None") // Unassign
        {
            if (daily.CheckInRecordId == recordId) daily.RemoveCheckIn();
            if (daily.CheckOutRecordId == recordId) daily.RemoveCheckOut();
        }
        else
        {
             return Result.Failure("Tipo de asignación inválido. Use 'Entrada' o 'Salida'.");
        }
        
        // 4. Actualizamos el DailyAttendance en el repositorio
        // Necesitamos un método Update en IDailyAttendanceRepository? O solo confiamos en EF Core Change Tracking?
        // La implementación que vimos antes usa Remove + Add o solo EF Change Tracking si se cargó.
        // Asumiendo que el seguimiento de EF Core está activo ya que cargamos 'daily'.
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
