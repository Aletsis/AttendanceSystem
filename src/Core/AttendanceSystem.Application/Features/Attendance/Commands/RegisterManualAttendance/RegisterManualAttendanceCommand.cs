using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;

public sealed record RegisterManualAttendanceCommand(
    string EmployeeId,
    DateTime CheckTime,
    string Type) : IRequest<Result>;

public sealed class RegisterManualAttendanceCommandHandler : IRequestHandler<RegisterManualAttendanceCommand, Result>
{
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IUnitOfWork _unitOfWork;


    public RegisterManualAttendanceCommandHandler(
        IAttendanceRepository attendanceRepo,
        IDailyAttendanceRepository dailyRepo,
        IUnitOfWork unitOfWork)
    {
        _attendanceRepo = attendanceRepo;
        _dailyRepo = dailyRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RegisterManualAttendanceCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var date = request.CheckTime.Date;

        // 1. Obtener Asistencia Diaria existente si ya fue procesada/creada
        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId,
            date,
            cancellationToken);

        // Si ya existe una asistencia diaria y tiene un registro previo del mismo tipo,
        // desasignamos el registro anterior (cambiando su estado a Pending)
        if (daily != null)
        {
            if (request.Type == "Entrada" && daily.CheckInRecordId != null)
            {
                var previousCheckIn = await _attendanceRepo.GetByIdAsync(daily.CheckInRecordId, cancellationToken);
                if (previousCheckIn != null)
                {
                    previousCheckIn.ResetStatus();
                    await _attendanceRepo.UpdateAsync(previousCheckIn, cancellationToken);
                }
            }
            else if (request.Type == "Salida" && daily.CheckOutRecordId != null)
            {
                var previousCheckOut = await _attendanceRepo.GetByIdAsync(daily.CheckOutRecordId, cancellationToken);
                if (previousCheckOut != null)
                {
                    previousCheckOut.ResetStatus();
                    await _attendanceRepo.UpdateAsync(previousCheckOut, cancellationToken);
                }
            }
        }

        // 2. Crear AttendanceRecord
        var manualDeviceId = DeviceId.From("MANUAL");

        var checkType = request.Type == "Entrada" ? CheckType.CheckIn : CheckType.CheckOut;

        var record = AttendanceRecord.Create(
            employeeId,
            manualDeviceId,
            request.CheckTime,
            VerifyMethod.Manual,
            checkType);

        // 3. Guardar Registro
        await _attendanceRepo.AddAsync(record, cancellationToken);

        // 4. Actualizar Asistencia Diaria si existe (SetCheckIn/SetCheckOut recalculan automáticamente el estado)
        if (daily != null)
        {
            if (request.Type == "Entrada")
            {
                daily.SetCheckIn(record.CheckTime, record.Id);
            }
            else if (request.Type == "Salida")
            {
                daily.SetCheckOut(record.CheckTime, record.Id);
            }

            // Marcar el nuevo registro como procesado
            record.MarkAsProcessed();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
