using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;

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

        // 1. Get Daily Attendance
        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId, 
            request.Date.ToDateTime(TimeOnly.MinValue), 
            cancellationToken);

        // If not found, we cannot assign manual override because we need the context (shift, etc)
        // User should "Process" first. 
        if (daily == null)
        {
            return Result.Failure("Debe procesar la asistencia del día antes de realizar asignaciones manuales.");
        }

        // 2. Get the specific Record
        var record = await _attendanceRepo.GetByIdAsync(recordId, cancellationToken);
        if (record == null)
        {
            return Result.Failure("Registro de asistencia no encontrado.");
        }

        // 3. Update Logic
        if (request.AssignmentType == "Entrada") // CheckIn
        {
            // If the same record was used as CheckOut, remove it from CheckOut first?
            // "Validating that there are not 2 entries or updates"
            // If there is already a CheckIn, we replace it.
            // If the record we are assigning is currently the CheckOut, we must clear CheckOut.
            
            if (daily.CheckOutRecordId == recordId)
            {
                daily.RemoveCheckOut();
            }

            daily.SetCheckIn(record.CheckTime, record.Id);
            
            // Mark record as processed
            if (record.Status != AttendanceSystem.Domain.Enumerations.AttendanceStatus.Processed)
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

            // Mark record as processed
            if (record.Status != AttendanceSystem.Domain.Enumerations.AttendanceStatus.Processed)
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
        
        // 4. Update Repo
        // We need an Update method on IDailyAttendanceRepository? 
        // The implementation we saw earlier uses Remove + Add or just EF Change Tracking if loaded.
        // Assuming EF Core tracking is active since we loaded 'daily'.
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
