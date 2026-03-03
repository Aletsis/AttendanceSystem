using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Attendance.Commands.UpdateDailyShift;

public sealed record UpdateDailyShiftCommand(
    string EmployeeId,
    DateOnly Date,
    Guid ShiftId) : IRequest<Result>;

public sealed class UpdateDailyShiftCommandHandler : IRequestHandler<UpdateDailyShiftCommand, Result>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IEmployeeRepository _employeeRepo;
    
    public UpdateDailyShiftCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IShiftRepository shiftRepo,
        IUnitOfWork unitOfWork,
        IAttendanceRepository attendanceRepo,
        IEmployeeRepository employeeRepo)
    {
        _dailyRepo = dailyRepo;
        _shiftRepo = shiftRepo;
        _unitOfWork = unitOfWork;
        _attendanceRepo = attendanceRepo;
        _employeeRepo = employeeRepo;
    }

    public async Task<Result> Handle(UpdateDailyShiftCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var shiftId = ShiftId.From(request.ShiftId);

        var employee = await _employeeRepo.GetByIdAsync(employeeId, cancellationToken);
        if (employee == null) return Result.Failure("Empleado no encontrado.");

        var shift = await _shiftRepo.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            return Result.Failure("El turno seleccionado no existe.");
        }

        var date = request.Date.ToDateTime(TimeOnly.MinValue);
        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(
            employeeId,
            date,
            cancellationToken);

        // Get records for that day to re-evaluate
        var searchStartDate = request.Date;
        var searchEndDate = searchStartDate;
        
        var dayStartTime = shift.StartTime;
        var dayEndTime = shift.EndTime;

        if (shift.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Mixto)
        {
            var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == date.DayOfWeek);
            if (dayConfig != null)
            {
                dayStartTime = dayConfig.StartTime;
                dayEndTime = dayConfig.EndTime;
            }
        }

        bool isNightShift = dayEndTime < dayStartTime;
        if (isNightShift)
        {
            searchEndDate = searchStartDate.AddDays(1);
        }

        var recordsEnumerable = await _attendanceRepo.GetByDateRangeAsync(
            searchStartDate, searchEndDate, employeeId, cancellationToken);
            
        var records = recordsEnumerable.OrderBy(r => r.CheckTime).ToList();

        // Release previously assigned logs so they can be re-assigned or kept
        if (daily != null)
        {
            if (daily.CheckInRecordId != null)
            {
                var r = await _attendanceRepo.GetByIdAsync(daily.CheckInRecordId, cancellationToken);
                if (r != null) { r.ResetStatus(); await _attendanceRepo.UpdateAsync(r, cancellationToken); }
            }
            if (daily.CheckOutRecordId != null)
            {
                var r = await _attendanceRepo.GetByIdAsync(daily.CheckOutRecordId, cancellationToken);
                if (r != null) { r.ResetStatus(); await _attendanceRepo.UpdateAsync(r, cancellationToken); }
            }
        }

        DateTime? checkIn = null;
        DateTime? checkOut = null;
        AttendanceSystem.Domain.Aggregates.AttendanceAggregate.AttendanceRecord? checkInRecord = null;
        AttendanceSystem.Domain.Aggregates.AttendanceAggregate.AttendanceRecord? checkOutRecord = null;

        if (records.Any())
        {
            var scheduledIn = date.Add(dayStartTime);
            var scheduledOut = date.Add(dayEndTime);
            if (isNightShift) scheduledOut = scheduledOut.AddDays(1);

            double maxInDistance = 300;
            double maxOutDistance = 960;

            IEnumerable<AttendanceSystem.Domain.Aggregates.AttendanceAggregate.AttendanceRecord> entryRecords = records;
            IEnumerable<AttendanceSystem.Domain.Aggregates.AttendanceAggregate.AttendanceRecord> exitRecords = records;

            if (isNightShift)
            {
                var entryWindowStart = date.Date.AddHours(12);
                var entryWindowEnd = date.Date.AddDays(1).AddSeconds(-1);
                entryRecords = records.Where(r => r.CheckTime >= entryWindowStart && r.CheckTime <= entryWindowEnd && (r.Status == AttendanceSystem.Domain.Enumerations.AttendanceStatus.Pending || (daily != null && r.Id == daily.CheckInRecordId)));

                var exitWindowStart = date.Date.AddDays(1);
                var exitWindowEnd = date.Date.AddDays(1).AddHours(12);
                exitRecords = records.Where(r => r.CheckTime >= exitWindowStart && r.CheckTime <= exitWindowEnd);
            }
            else
            {
                entryRecords = records.Where(r => r.Status == AttendanceSystem.Domain.Enumerations.AttendanceStatus.Pending || (daily != null && r.Id == daily.CheckInRecordId));
                exitRecords = records.Where(r => r.Status == AttendanceSystem.Domain.Enumerations.AttendanceStatus.Pending || (daily != null && r.Id == daily.CheckOutRecordId));
            }

            var matchIn = entryRecords
                .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledIn).TotalMinutes) })
                .Where(x => x.Diff <= maxInDistance)
                .OrderBy(x => x.Diff).FirstOrDefault();

            if (matchIn != null)
            {
                checkInRecord = matchIn.Record;
                checkIn = checkInRecord.CheckTime;
            }

            var matchOut = exitRecords
                .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledOut).TotalMinutes) })
                .Where(x => x.Diff <= maxOutDistance)
                .OrderBy(x => x.Diff).FirstOrDefault();

            if (matchOut != null)
            {
                if (checkInRecord != null && matchOut.Record.Id == checkInRecord.Id)
                {
                    if (matchOut.Diff < matchIn!.Diff) { checkOutRecord = matchOut.Record; checkOut = checkOutRecord.CheckTime; checkIn = null; checkInRecord = null; }
                }
                else
                {
                    checkOutRecord = matchOut.Record; checkOut = checkOutRecord.CheckTime;
                }
            }

            if (checkIn.HasValue && checkOut.HasValue && checkOut.Value <= checkIn.Value)
            {
                 if (Math.Abs((checkIn.Value - scheduledIn).TotalMinutes) <= Math.Abs((checkOut.Value - scheduledOut).TotalMinutes)) { checkOut = null; checkOutRecord = null; }
                 else { checkIn = null; checkInRecord = null; }
            }
        }

        if (checkInRecord != null)
        {
            checkInRecord.MarkAsProcessed();
            checkInRecord.SetInferredType(AttendanceSystem.Domain.Enumerations.CheckType.CheckIn);
            await _attendanceRepo.UpdateAsync(checkInRecord, cancellationToken);
        }

        if (checkOutRecord != null)
        {
            checkOutRecord.MarkAsProcessed();
            checkOutRecord.SetInferredType(AttendanceSystem.Domain.Enumerations.CheckType.CheckOut);
            await _attendanceRepo.UpdateAsync(checkOutRecord, cancellationToken);
        }

        var isRestDay = false;
        if (employee.RestDay.HasValue)
        {
            var dayOfWeek = (AttendanceSystem.Domain.Enumerations.WeekDay)(int)date.DayOfWeek;
            if (employee.RestDay == dayOfWeek) isRestDay = true;
        }

        if (daily == null)
        {
            daily = DailyAttendance.Create(
                employeeId,
                date,
                shift,
                checkIn,
                checkOut,
                isRestDay,
                checkInRecord?.Id,
                checkOutRecord?.Id,
                employee.CalculateOvertimeBeforeEntry
            );
            _dailyRepo.Add(daily);
        }
        else
        {
            daily.UpdateShift(shift);
            daily.SetRestDayOverride(isRestDay);
            if (checkInRecord != null && checkIn.HasValue) daily.SetCheckIn(checkIn.Value, checkInRecord.Id); else daily.RemoveCheckIn();
            if (checkOutRecord != null && checkOut.HasValue) daily.SetCheckOut(checkOut.Value, checkOutRecord.Id); else daily.RemoveCheckOut();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return Result.Success();
    }
}
