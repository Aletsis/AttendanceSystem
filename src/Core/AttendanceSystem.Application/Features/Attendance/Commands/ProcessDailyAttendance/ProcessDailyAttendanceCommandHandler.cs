using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;


public class ProcessDailyAttendanceCommandHandler : IRequestHandler<ProcessDailyAttendanceCommand, int>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProcessDailyAttendanceCommandHandler> _logger;

    public ProcessDailyAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IEmployeeRepository employeeRepo,
        IShiftRepository shiftRepo,
        IUnitOfWork unitOfWork,
        ILogger<ProcessDailyAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _employeeRepo = employeeRepo;
        _shiftRepo = shiftRepo;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }


    public async Task<int> Handle(ProcessDailyAttendanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Iniciando procesamiento de asistencia diaria. Rango: {StartDate} - {EndDate}, BranchId: {BranchId}, EmployeeId: {EmployeeId}",
            request.StartDate,
            request.EndDate,
            request.BranchId?.Value,
            request.EmployeeId?.Value);

        int processedCount = 0;
        
        // 1. Get all employees
        var employees = (await _employeeRepo.GetAllAsync(cancellationToken)).ToList();
        _logger.LogDebug("Obtenidos {EmployeeCount} empleados de la base de datos", employees.Count);

        // Filter by Branch if specified
        if (request.BranchId != null)
        {
            employees = employees.Where(e => e.BranchId == request.BranchId).ToList();
            _logger.LogDebug("Filtrado por sucursal {BranchId}: {EmployeeCount} empleados", request.BranchId.Value, employees.Count);
        }

        // Filter by Employee if specified
        if (request.EmployeeId != null)
        {
            employees = employees.Where(e => e.Id == request.EmployeeId).ToList();
            _logger.LogDebug("Filtrado por empleado {EmployeeId}: {EmployeeCount} empleados", request.EmployeeId.Value, employees.Count);
        }
        
        var totalDays = (request.EndDate.Date - request.StartDate.Date).Days + 1;
        _logger.LogInformation(
            "Procesando asistencia para {EmployeeCount} empleados durante {DayCount} días",
            employees.Count,
            totalDays);
        
        // 2. Iterate dates
        for (var date = request.StartDate.Date; date <= request.EndDate.Date; date = date.AddDays(1))
        {
            foreach (var employee in employees)
            {
                // Skip if not active? 
                if (employee.Status != EmployeeStatus.Alta) continue; // Basic filter

                // 2.1 Clean up existing processing for this day (Re-processing Logic)
                // We must free up the AttendanceRecords so they can be re-evaluated or picked up by correct logic.
                var existingDA = await _dailyRepo.GetByEmployeeAndDateAsync(employee.Id, date, cancellationToken);
                if (existingDA != null)
                {
                    if (existingDA.CheckInRecordId != null)
                    {
                        var r = await _attendanceRepo.GetByIdAsync(existingDA.CheckInRecordId, cancellationToken);
                        if (r != null)
                        {
                            r.ResetStatus();
                            await _attendanceRepo.UpdateAsync(r, cancellationToken);
                        }
                    }
                    if (existingDA.CheckOutRecordId != null)
                    {
                        var r = await _attendanceRepo.GetByIdAsync(existingDA.CheckOutRecordId, cancellationToken);
                        if (r != null)
                        {
                            r.ResetStatus();
                            await _attendanceRepo.UpdateAsync(r, cancellationToken);
                        }
                    }
                    _dailyRepo.Remove(existingDA);
                }

                // 3. Determine Shift & Search Scope Logic
                Shift? shift = null;
                bool isRestDay = false;
                var searchStartDate = DateOnly.FromDateTime(date);
                var searchEndDate = searchStartDate; // Default to single day

                if (employee.RestDay.HasValue)
                {
                    // Map DayOfWeek
                    var dayOfWeek = (AttendanceSystem.Domain.Enumerations.WeekDay)(int)date.DayOfWeek; 
                    if (employee.RestDay == dayOfWeek)
                    {
                        isRestDay = true;
                    }
                }

                if (!isRestDay && employee.ScheduleId != null)
                {
                    shift = await _shiftRepo.GetByIdAsync(employee.ScheduleId, cancellationToken);
                }

                // Check for Night Shift (e.g., 22:00 - 06:00)
                // If it's a night shift, we extend search to the next day to catch the exit
                bool isNightShift = false;
                if (shift != null && shift.EndTime < shift.StartTime)
                {
                    isNightShift = true;
                    searchEndDate = searchStartDate.AddDays(1);
                }

                // 4. Fetch Records
                var recordsEnumerable = await _attendanceRepo.GetByDateRangeAsync(searchStartDate, searchEndDate, employee.Id, cancellationToken);
                // Important: If night shift, we might have many records. Use List to process.
                // Filter out records that are already processed (claimed by other runs/days), EXCEPT those we just reset? 
                // Since we reset ours above, they are Pending. Records from OTHER days that overlap are Processed.
                // Filter out records that are already processed? 
                // NO. For Night Shifts and correction scenarios, we must be able to "steal" or "re-claim" 
                // a record that was incorrectly claimed by another day (e.g. Day 2 claiming Day 1's Exit as its Entry).
                // We rely on the Stricter Tolerances (Asymmetric) to ensure we only claim what truly fits.
                var records = recordsEnumerable
                    .OrderBy(r => r.CheckTime)
                    .ToList();

                // 5. Determine Actual In/Out
                DateTime? checkIn = null;
                DateTime? checkOut = null;
                AttendanceRecord? checkInRecord = null;
                AttendanceRecord? checkOutRecord = null;

                if (shift != null && !isRestDay && records.Any())
                {
                    // "Best Fit" Logic using Scheduled Times
                    var scheduledIn = date.Add(shift.StartTime);
                    var scheduledOut = date.Add(shift.EndTime);
                    if (isNightShift) 
                    { 
                        scheduledOut = scheduledOut.AddDays(1); 
                    }

                    // Define tolerance windows
                    double maxInDistance = 300;   // 5 hours max early/late for CheckIn
                    double maxOutDistance = 960;  // 16 hours max for CheckOut (allows double shifts)

                    // For Night Shifts, use TIME WINDOWS to prevent mismatching
                    // Entry should be in the evening/night, Exit should be in the early morning
                    IEnumerable<AttendanceRecord> entryRecords = records;
                    IEnumerable<AttendanceRecord> exitRecords = records;

                    if (isNightShift)
                    {
                        // Entry Window: From noon of current day to end of day (12:00 - 23:59)
                        // This prevents early morning records (like 6:20 AM) from being matched as entries
                        var entryWindowStart = date.Date.AddHours(12);
                        var entryWindowEnd = date.Date.AddDays(1).AddSeconds(-1);
                        
                        entryRecords = records.Where(r => 
                            r.CheckTime >= entryWindowStart && 
                            r.CheckTime <= entryWindowEnd &&
                            r.Status == AttendanceStatus.Pending); // Only use unprocessed records for entry

                        // Exit Window: From start of next day to noon (00:00 - 12:00)
                        // This ensures we only look for exits in the morning period
                        var exitWindowStart = date.Date.AddDays(1);
                        var exitWindowEnd = date.Date.AddDays(1).AddHours(12);
                        
                        exitRecords = records.Where(r => 
                            r.CheckTime >= exitWindowStart && 
                            r.CheckTime <= exitWindowEnd);
                        // Note: We allow already processed records for exit ONLY if we're reprocessing
                        // This handles the case where we need to reclaim an exit that was wrongly assigned
                    }
                    else
                    {
                        // For regular shifts, only use pending records to avoid stealing from other days
                        entryRecords = records.Where(r => r.Status == AttendanceStatus.Pending);
                        exitRecords = records.Where(r => r.Status == AttendanceStatus.Pending);
                    }

                    // Find best candidate for IN
                    var matchIn = entryRecords
                        .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledIn).TotalMinutes) })
                        .Where(x => x.Diff <= maxInDistance)
                        .OrderBy(x => x.Diff)
                        .FirstOrDefault();

                    if (matchIn != null)
                    {
                        checkInRecord = matchIn.Record;
                        checkIn = matchIn.Record.CheckTime;
                    }

                    // Find best candidate for OUT
                    var matchOut = exitRecords
                        .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledOut).TotalMinutes) })
                        .Where(x => x.Diff <= maxOutDistance)
                        .OrderBy(x => x.Diff)
                        .FirstOrDefault();

                    if (matchOut != null)
                    {
                        // Check for overlap (same record matched as both IN and OUT)
                        if (checkInRecord != null && matchOut.Record.Id == checkInRecord.Id)
                        {
                            // Decide based on which is closer
                            if (matchOut.Diff < matchIn!.Diff)
                            {
                                // Closer to Out -> It's an Out
                                checkOutRecord = matchOut.Record;
                                checkOut = matchOut.Record.CheckTime;
                                checkIn = null;
                                checkInRecord = null;
                            }
                            // Else keep as In
                        }
                        else
                        {
                            checkOutRecord = matchOut.Record;
                            checkOut = matchOut.Record.CheckTime;
                        }
                    }

                    // Logic Check: Ensure Out is after In
                    if (checkIn.HasValue && checkOut.HasValue && checkOut.Value <= checkIn.Value)
                    {
                         // Discard the one that has the larger deviation from its target
                         double diffIn = Math.Abs((checkIn.Value - scheduledIn).TotalMinutes);
                         double diffOut = Math.Abs((checkOut.Value - scheduledOut).TotalMinutes);

                         if (diffIn <= diffOut)
                         {
                             checkOut = null;
                             checkOutRecord = null;
                         }
                         else
                         {
                             checkIn = null;
                             checkInRecord = null;
                         }
                    }
                }
                else if (records.Any())
                {
                    // Fallback for No-Shift / Rest Day: use simple First/Last of the *first* day (searchStartDate)
                    // Filter to date only to behave like calendar day
                    var dayRecords = records.Where(r => r.CheckTime.Date == date.Date).ToList();

                    if (dayRecords.Any())
                    {
                        var first = dayRecords.First();
                        checkIn = first.CheckTime;
                        checkInRecord = first;

                        if (dayRecords.Count > 1)
                        {
                            var last = dayRecords.Last();
                            checkOut = last.CheckTime;
                            checkOutRecord = last;
                        }
                    }
                }

                // Marcar registros como procesados y asignar tipo
                if (checkInRecord != null) // Status check removed as we filtered or reset them
                {
                    // If it was already processed (rare race cond), we might overwrite or fail.
                    // But we filtered Processed out. So it is Pending.
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

                // 6. Create DailyAttendance
                var dailyAttendance = DailyAttendance.Create(
                    employee.Id,
                    date,
                    shift,
                    checkIn,
                    checkOut,
                    isRestDay,
                    checkInRecord?.Id,
                    checkOutRecord?.Id);

                // 7. Save or Update
                // 7. Save
                // existingDA was already removed at the start of loop if present.
                _dailyRepo.Add(dailyAttendance);
                processedCount++;
            }
        }
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation(
            "Procesamiento de asistencia diaria completado. Registros procesados: {ProcessedCount}",
            processedCount);
        
        return processedCount;
    }
}
