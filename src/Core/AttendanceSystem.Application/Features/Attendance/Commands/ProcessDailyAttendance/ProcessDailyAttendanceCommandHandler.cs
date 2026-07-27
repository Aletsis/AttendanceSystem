using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;


public class ProcessDailyAttendanceCommandHandler : IRequestHandler<ProcessDailyAttendanceCommand, int>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISender _sender;
    private readonly ILogger<ProcessDailyAttendanceCommandHandler> _logger;

    public ProcessDailyAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IEmployeeRepository employeeRepo,
        IShiftRepository shiftRepo,
        IUnitOfWork unitOfWork,
        ISender sender,
        ILogger<ProcessDailyAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _employeeRepo = employeeRepo;
        _shiftRepo = shiftRepo;
        _unitOfWork = unitOfWork;
        _sender = sender;
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

        // 1.1 Bulk load all shifts
        var shifts = (await _shiftRepo.GetAllAsync(cancellationToken))
            .ToDictionary(s => s.Id);
        _logger.LogDebug("Obtenidos {ShiftCount} turnos para mapeo en memoria", shifts.Count);

        // 1.2 Bulk load all existing DailyAttendance records for the date range
        var existingDailyAttendances = await _dailyRepo.GetByDateRangeAsync(
            request.StartDate, 
            request.EndDate, 
            request.BranchId, 
            request.EmployeeId, 
            cancellationToken);

        var existingDaLookup = existingDailyAttendances
            .GroupBy(da => (da.EmployeeId.Value, da.Date.Date))
            .ToDictionary(g => g.Key, g => g.First());
        _logger.LogDebug("Obtenidos {DaCount} registros de asistencia diaria existentes para reprogramación", existingDailyAttendances.Count);

        // 1.3 Bulk load all attendance records for the date range (including a buffer day before and after for cross-day shifts)
        var startQueryDate = DateOnly.FromDateTime(request.StartDate.Date.AddDays(-1));
        var endQueryDate = DateOnly.FromDateTime(request.EndDate.Date.AddDays(2));

        var processedEmployeeIds = employees.Select(e => e.Id).ToHashSet();
        
        IReadOnlyList<AttendanceRecord> allRecords;
        if (request.EmployeeId != null)
        {
            allRecords = await _attendanceRepo.GetByDateRangeAsync(
                startQueryDate, 
                endQueryDate, 
                request.EmployeeId, 
                cancellationToken);
        }
        else
        {
            allRecords = await _attendanceRepo.GetByDateRangeAsync(
                startQueryDate, 
                endQueryDate, 
                null, 
                cancellationToken);
        }

        var filteredRecords = allRecords
            .Where(r => processedEmployeeIds.Contains(r.EmployeeId))
            .ToList();

        var recordsByEmployee = filteredRecords
            .GroupBy(r => r.EmployeeId.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var recordsById = filteredRecords
            .ToDictionary(r => r.Id.Value);
        
        _logger.LogDebug("Obtenidos {RecordCount} registros biométricos filtrados", filteredRecords.Count);
        
        // 2. Iterate dates
        for (var date = request.StartDate.Date; date <= request.EndDate.Date; date = date.AddDays(1))
        {
            foreach (var employee in employees)
            {
                // Skip if not active? 
                if (employee.Status != EmployeeStatus.Alta) continue; // Basic filter

                // 2.1 Clean up existing processing for this day (Re-processing Logic)
                // We must free up the AttendanceRecords so they can be re-evaluated or picked up by correct logic.
                var lookupKey = (employee.Id.Value, date.Date);
                if (existingDaLookup.TryGetValue(lookupKey, out var existingDA))
                {
                    if (existingDA.CheckInRecordId != null && recordsById.TryGetValue(existingDA.CheckInRecordId.Value, out var checkInRec))
                    {
                        checkInRec.ResetStatus();
                        await _attendanceRepo.UpdateAsync(checkInRec, cancellationToken);
                    }
                    if (existingDA.CheckOutRecordId != null && recordsById.TryGetValue(existingDA.CheckOutRecordId.Value, out var checkOutRec))
                    {
                        checkOutRec.ResetStatus();
                        await _attendanceRepo.UpdateAsync(checkOutRec, cancellationToken);
                    }
                    _dailyRepo.Remove(existingDA);
                }

                // 3. Determine Shift & Search Scope Logic
                Shift? shift = null;
                bool isRestDay = false;
                var searchStartDate = DateOnly.FromDateTime(date);
                var searchEndDate = searchStartDate; // Default to single day

                if (employee.ScheduleId != null && shifts.TryGetValue(employee.ScheduleId, out var matchedShift))
                {
                    shift = matchedShift;
                }

                // Check for Night Shift or Continuous (Cross-Day)
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

                            // If dayConfig is Nocturno or Continuo, or if endTime <= startTime, it crosses day
                            if (dayEndTime <= dayStartTime || dayConfig.ShiftType == ShiftType.Nocturno || dayConfig.ShiftType == ShiftType.Continuo)
                            {
                                isCrossDay = true;
                            }
                        }
                    }
                    else if (dayEndTime <= dayStartTime || shift.ShiftType == ShiftType.Nocturno || shift.ShiftType == ShiftType.Continuo)
                    {
                        isCrossDay = true;
                    }

                    if (isCrossDay)
                    {
                        searchEndDate = searchStartDate.AddDays(1);
                    }
                }

                // 4. Fetch Records in memory
                var employeeRecords = recordsByEmployee.TryGetValue(employee.Id.Value, out var empRecs)
                    ? empRecs
                    : new List<AttendanceRecord>();

                var startDateTime = searchStartDate.ToDateTime(TimeOnly.MinValue);
                var endDateTime = searchEndDate.ToDateTime(TimeOnly.MaxValue);

                var records = employeeRecords
                    .Where(r => r.CheckTime >= startDateTime && r.CheckTime <= endDateTime)
                    .OrderBy(r => r.CheckTime)
                    .ToList();

                // Determine if today is a rest day
                if (employee.RestDay.HasValue)
                {
                    var dayOfWeek = (AttendanceSystem.Domain.Enumerations.WeekDay)(int)date.DayOfWeek; 
                    if (employee.RestDay == dayOfWeek)
                    {
                        isRestDay = true;
                    }
                }

                // 5. Delegate calculation to specific sub-command
                if (shift == null)
                {
                    await _sender.Send(new ProcessNoShiftAttendanceCommand(
                        employee,
                        date,
                        records,
                        isRestDay), cancellationToken);
                }
                else if (shift.ShiftType == ShiftType.Mixto)
                {
                    await _sender.Send(new ProcessMixAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay), cancellationToken);
                }
                else if (shift.ShiftType == ShiftType.Continuo)
                {
                    await _sender.Send(new ProcessContinuousAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay), cancellationToken);
                }
                else if (shift.ShiftType == ShiftType.Nocturno)
                {
                    await _sender.Send(new ProcessNightlyAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay,
                        dayStartTime,
                        dayEndTime), cancellationToken);
                }
                else
                {
                    // Matutino o Vespertino
                    await _sender.Send(new ProcessRegularAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay), cancellationToken);
                }

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
