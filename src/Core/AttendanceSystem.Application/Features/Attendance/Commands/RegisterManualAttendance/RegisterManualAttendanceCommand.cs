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
    private readonly IDeviceRepository _deviceRepo; // To find a "Virtual" device or similar if needed. We'll use a dummy ID or find first. 

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

        // 1. Check/Get Daily Attendance to validate duplication
        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId,
            date,
            cancellationToken);

        // Required logic: "No puede haber 2 entradas ni 2 salidas"
        // This implies validating against *Processed* attendance.
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
            // If checking strict "Processed" existence, if DailyAttendance doesn't exist, we can't violate it. 
            // BUT, the user might mean "Raw Logs" too? "No puede haber 2 entradas" usually means Processed.
            // If Daily isn't processed yet, we can add the log safely potentially. 
            // But if we want to *force* assignment, we might need Daily to exist?
            // Let's assume we proceed to Create Log.
        }

        // 2. Create AttendanceRecord
        // We need a DeviceId. We can use a special "Manual" device or just a GUID zeros.
        // For now, let's use a zero GUID to indicate "System/Manual". 
        // Or wait, DeviceId is strongly typed. 
        var manualDeviceId = DeviceId.From("MANUAL");

        var checkType = request.Type == "Entrada" ? CheckType.CheckIn : CheckType.CheckOut;
        
        var record = AttendanceRecord.Create(
            employeeId,
            manualDeviceId,
            request.CheckTime,
            VerifyMethod.Manual,
            checkType);

        // Mark as Processed immediately since we are manually registering it for a purpose
        // checkType logic above is approximate, CheckType enum has StartBreak etc. User only said Entry/Exit options.

        // 3. Save Record
        await _attendanceRepo.AddAsync(record, cancellationToken);
        
        // 4. Update DailyAttendance if it exists
        // If it doesn't exist, we can't update it. The user will have to "Process" later.
        // Or should we create it? Usually "Process" command creates it. 
        // If we only add the log, the user can then "Process" and it will be picked up.
        // BUT, the validation requirement suggests we want to ensure consistency NOW.
        // If Daily exists, we can update it.
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
            
            // Mark record as processed
            record.MarkAsProcessed();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
