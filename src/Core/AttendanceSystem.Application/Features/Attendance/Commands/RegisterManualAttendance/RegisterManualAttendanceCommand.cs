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

        // 1. Combrobar/Obtener Asistencia Diaria para validar duplicación
        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId,
            date,
            cancellationToken);

        // Logica requerida: "No puede haber 2 entradas ni 2 salidas"
        // Esto implica validar contra la asistencia *procesada*.
        if (daily != null)
        {
            if (request.Type == "Entrada" && daily.ActualCheckIn.HasValue)
            {
                return Result.Failure($"Ya existe una Entrada registrada para el usuario el día {date:dd/MM/yyyy} a las {daily.ActualCheckIn.Value:HH:mm}.");
            }
            if (request.Type == "Salida" && daily.ActualCheckOut.HasValue)
            {
                return Result.Failure($"Ya existe una Salida registrada para el usuario el día {date:dd/MM/yyyy} a las {daily.ActualCheckOut.Value:HH:mm}.");
            }
        }
        else
        {
            
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
        
        // 4. Actualizar Asistencia Diaria si existe
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
            
            // Marcar el registro como procesado
            record.MarkAsProcessed();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
