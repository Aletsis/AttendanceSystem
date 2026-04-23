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

                if (employee.ScheduleId != null)
                {
                    shift = await _shiftRepo.GetByIdAsync(employee.ScheduleId, cancellationToken);
                }

                // Check for Night Shift or 24h Shift (Cross-Day)
                // If it's a cross-day shift, we extend search to the next day to catch the exit
                bool isCrossDay = false;
                TimeSpan dayStartTime = TimeSpan.Zero;
                TimeSpan dayEndTime = TimeSpan.Zero;
                
                if (shift != null)
                {
                    dayStartTime = shift.StartTime;
                    dayEndTime = shift.EndTime;

                    if (shift.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Mixto)
                    {
                        var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == date.DayOfWeek);
                        if (dayConfig != null)
                        {
                            dayStartTime = dayConfig.StartTime;
                            dayEndTime = dayConfig.EndTime;
                        }
                    }

                    if (dayEndTime <= dayStartTime || shift.ShiftType == ShiftType.Jornada24h || shift.ShiftType == ShiftType.Continuo)
                    {
                        isCrossDay = true;
                        searchEndDate = searchStartDate.AddDays(1);
                    }
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

                if (shift != null && records.Any())
                {
                    // "Best Fit" Logic using Scheduled Times
                    var scheduledIn = date.Add(dayStartTime);
                    var scheduledOut = date.Add(dayEndTime);
                    if (isCrossDay) 
                    { 
                        scheduledOut = scheduledOut.AddDays(1); 
                    }

                    // Define tolerance windows
                    double maxInDistance = 300;   // 5 hours max early/late for CheckIn
                    double maxOutDistance = 960;  // 16 hours max for CheckOut (allows double shifts)

                    // For Cross-Day Shifts, use RELATIVE TIME WINDOWS to prevent mismatching
                    IEnumerable<AttendanceRecord> entryRecords = records;
                    IEnumerable<AttendanceRecord> exitRecords = records;

                    if (isCrossDay)
                    {
                        if (shift.ShiftType == ShiftType.Continuo)
                        {
                            // FLEXIBLE Logic: First of the day -> Next available within 24h
                            // Note: we only look for the entry on the 'date' being processed
                            var potentialIn = records
                                .Where(r => r.CheckTime.Date == date.Date && r.Status == AttendanceStatus.Pending)
                                .OrderBy(r => r.CheckTime)
                                .FirstOrDefault();

                            if (potentialIn != null)
                            {
                                checkInRecord = potentialIn;
                                checkIn = potentialIn.CheckTime;

                                // Search for ANY next record of this employee within 24 hours
                                checkOutRecord = records
                                    .Where(r => r.CheckTime > checkIn.Value && (r.CheckTime - checkIn.Value).TotalHours <= 24)
                                    .OrderBy(r => r.CheckTime)
                                    .FirstOrDefault();
                                
                                if (checkOutRecord != null)
                                {
                                    checkOut = checkOutRecord.CheckTime;
                                }
                            }
                        }
                        else
                        {
                            // RELATIVE WINDOWS Logic for Cross-Day / Night Shifts
                            // Entry Window: +/- 6 hours around scheduled In
                            var entryWindowStart = scheduledIn.AddHours(-6);
                            var entryWindowEnd = scheduledIn.AddHours(6);
                            
                            entryRecords = records.Where(r => 
                                r.CheckTime >= entryWindowStart && 
                                r.CheckTime <= entryWindowEnd &&
                                r.Status == AttendanceStatus.Pending); // Only use unprocessed records for entry

                            // Exit Window: +/- 10 hours around scheduled Out
                            var exitWindowStart = scheduledOut.AddHours(-10);
                            var exitWindowEnd = scheduledOut.AddHours(10);
                            
                            exitRecords = records.Where(r => 
                                r.CheckTime >= exitWindowStart && 
                                r.CheckTime <= exitWindowEnd);
                            
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
                                        checkOutRecord = matchOut.Record;
                                        checkOut = matchOut.Record.CheckTime;
                                        checkIn = null;
                                        checkInRecord = null;
                                    }
                                }
                                else
                                {
                                    checkOutRecord = matchOut.Record;
                                    checkOut = matchOut.Record.CheckTime;
                                }
                            }
                        }
                    }
                    else
                    {
                        // For regular shifts, only use pending records to avoid stealing from other days
                        entryRecords = records.Where(r => r.Status == AttendanceStatus.Pending);
                        exitRecords = records.Where(r => r.Status == AttendanceStatus.Pending);
                        
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
                            if (checkInRecord != null && matchOut.Record.Id == checkInRecord.Id)
                            {
                                if (matchOut.Diff < matchIn!.Diff) { checkOutRecord = matchOut.Record; checkOut = checkOutRecord.CheckTime; checkIn = null; checkInRecord = null; }
                            }
                            else
                            {
                                checkOutRecord = matchOut.Record; checkOut = checkOutRecord.CheckTime;
                            }
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

                if (employee.RestDay.HasValue)
                {
                    // Map DayOfWeek
                    var dayOfWeek = (AttendanceSystem.Domain.Enumerations.WeekDay)(int)date.DayOfWeek; 
                    if (employee.RestDay == dayOfWeek)
                    {
                        isRestDay = true;
                    }
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
                    checkOutRecord?.Id,
                    employee.CalculateOvertimeBeforeEntry,
                    employee.OvertimeAuthorized);

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
