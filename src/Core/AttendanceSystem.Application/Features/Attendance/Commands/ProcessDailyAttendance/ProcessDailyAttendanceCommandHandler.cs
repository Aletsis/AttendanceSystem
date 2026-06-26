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

                // 5. Determine Actual In/Out
                DateTime? checkIn = null;
                DateTime? checkOut = null;
                AttendanceRecord? checkInRecord = null;
                AttendanceRecord? checkOutRecord = null;
                // Resultado del análisis de registros intermedios (comida y permisos temporales)
                (int Lunch, bool HasTempExits, int TempMinutes)? intermediateAnalysis = null;

                if (shift != null && records.Any())
                {
                    if (isCrossDay)
                    {
                        // --- TURNOS NOCTURNOS / 24H: lógica de ventanas de tiempo (sin cambios) ---
                        var scheduledIn = date.Add(dayStartTime);
                        var scheduledOut = date.Add(dayEndTime);
                        scheduledOut = scheduledOut.AddDays(1);

                        double maxInDistance = 300;
                        double maxOutDistance = 960;

                        IEnumerable<AttendanceRecord> entryRecords = records;
                        IEnumerable<AttendanceRecord> exitRecords = records;

                        if (shift.ShiftType == ShiftType.Continuo)
                        {
                            var potentialIn = records
                                .Where(r => r.CheckTime.Date == date.Date && r.Status == AttendanceStatus.Pending)
                                .OrderBy(r => r.CheckTime)
                                .FirstOrDefault();

                            if (potentialIn != null)
                            {
                                checkInRecord = potentialIn;
                                checkIn = potentialIn.CheckTime;

                                checkOutRecord = records
                                    .Where(r => r.CheckTime > checkIn.Value && (r.CheckTime - checkIn.Value).TotalHours <= 24)
                                    .OrderBy(r => r.CheckTime)
                                    .FirstOrDefault();

                                if (checkOutRecord != null)
                                    checkOut = checkOutRecord.CheckTime;
                            }
                        }
                        else
                        {
                            var entryWindowStart = scheduledIn.AddHours(-6);
                            var entryWindowEnd = scheduledIn.AddHours(6);

                            entryRecords = records.Where(r =>
                                r.CheckTime >= entryWindowStart &&
                                r.CheckTime <= entryWindowEnd &&
                                r.Status == AttendanceStatus.Pending);

                            var exitWindowStart = scheduledOut.AddHours(-10);
                            var exitWindowEnd = scheduledOut.AddHours(10);

                            exitRecords = records.Where(r =>
                                r.CheckTime >= exitWindowStart &&
                                r.CheckTime <= exitWindowEnd);

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

                            var matchOut = exitRecords
                                .Select(r => new { Record = r, Diff = Math.Abs((r.CheckTime - scheduledOut).TotalMinutes) })
                                .Where(x => x.Diff <= maxOutDistance)
                                .OrderBy(x => x.Diff)
                                .FirstOrDefault();

                            if (matchOut != null)
                            {
                                if (checkInRecord != null && matchOut.Record.Id == checkInRecord.Id)
                                {
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

                        // Sanity check: salida después de entrada
                        if (checkIn.HasValue && checkOut.HasValue && checkOut.Value <= checkIn.Value)
                        {
                            var scheduledIn2 = date.Add(dayStartTime);
                            var scheduledOut2 = date.Add(dayEndTime).AddDays(1);
                            double diffIn = Math.Abs((checkIn.Value - scheduledIn2).TotalMinutes);
                            double diffOut = Math.Abs((checkOut.Value - scheduledOut2).TotalMinutes);
                            if (diffIn <= diffOut) { checkOut = null; checkOutRecord = null; }
                            else { checkIn = null; checkInRecord = null; }
                        }
                    }
                    else
                    {
                        // ---------------------------------------------------------------
                        // TURNOS REGULARES: Algoritmo First-In / Last-Out
                        // ---------------------------------------------------------------
                        // Solo usar registros Pending del día actual
                        var pendingDayRecords = records
                            .Where(r => r.Status == AttendanceStatus.Pending && r.CheckTime.Date == date.Date)
                            .OrderBy(r => r.CheckTime)
                            .ToList();

                        if (pendingDayRecords.Count >= 1)
                        {
                            // PASO 1: Primer registro = Entrada oficial, Último = Salida oficial
                            checkInRecord = pendingDayRecords.First();
                            checkIn = checkInRecord.CheckTime;

                            if (pendingDayRecords.Count >= 2)
                            {
                                checkOutRecord = pendingDayRecords.Last();
                                checkOut = checkOutRecord.CheckTime;
                            }

                            // PASO 2: Registros intermedios (entre entrada y salida)
                            var middleRecords = pendingDayRecords.Count >= 3
                                ? pendingDayRecords.Skip(1).SkipLast(1).ToList()
                                : new List<AttendanceRecord>();

                            if (middleRecords.Any())
                            {
                                // PASO 2A: Pre-filtro — descartar dobles toques por proximidad a los extremos
                                const int doubleTapThresholdMinutes = 15;

                                var entryDoubleTaps = middleRecords
                                    .Where(r => (r.CheckTime - checkIn!.Value).TotalMinutes <= doubleTapThresholdMinutes)
                                    .ToList();

                                var exitDoubleTaps = checkOut.HasValue
                                    ? middleRecords
                                        .Where(r => (checkOut.Value - r.CheckTime).TotalMinutes <= doubleTapThresholdMinutes)
                                        .ToList()
                                    : new List<AttendanceRecord>();

                                var recordsToAnalyze = middleRecords
                                    .Except(entryDoubleTaps)
                                    .Except(exitDoubleTaps)
                                    .OrderBy(r => r.CheckTime)
                                    .ToList();

                                _logger.LogDebug(
                                    "Empleado {EmpId} - {Date}: {Total} intermedios. DoubleTap entrada={DTIn}, salida={DTOut}. Para analizar={ToAnalyze}",
                                    employee.Id.Value, date.ToString("dd/MM/yyyy"),
                                    middleRecords.Count, entryDoubleTaps.Count, exitDoubleTaps.Count, recordsToAnalyze.Count);

                                // PASO 2B: Análisis de pares de ausencia
                                int lunchMinutesDeducted = 0;
                                bool hasTemporaryExits = false;
                                int temporaryExitMinutes = 0;

                                // Iterar en pares (salida intermedia, regreso)
                                for (int i = 0; i < recordsToAnalyze.Count; i += 2)
                                {
                                    var exitRecord = recordsToAnalyze[i];

                                    if (i + 1 < recordsToAnalyze.Count)
                                    {
                                        // Par completo: calcular duración de ausencia
                                        var returnRecord = recordsToAnalyze[i + 1];
                                        double absenceMinutes = (returnRecord.CheckTime - exitRecord.CheckTime).TotalMinutes;

                                        if (absenceMinutes <= 15)
                                        {
                                            // Error de doble checada intermedia — ignorar
                                            _logger.LogDebug("Par intermedio ({Exit}-{Return}): {Min} min → doble toque intermedio, ignorado.",
                                                exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes);
                                        }
                                        else if (absenceMinutes <= 90)
                                        {
                                            // Posible permiso temporal o comida corta
                                            hasTemporaryExits = true;
                                            temporaryExitMinutes += (int)absenceMinutes;
                                            _logger.LogInformation("Par intermedio ({Exit}-{Return}): {Min} min → posible permiso temporal detectado.",
                                                exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes);
                                        }
                                        else
                                        {
                                            // Comida formal: aplicar deducción configurada en el turno
                                            if (shift.LunchBreakMinutes > 0)
                                            {
                                                lunchMinutesDeducted += shift.LunchBreakMinutes;
                                                _logger.LogDebug("Par intermedio ({Exit}-{Return}): {Min} min → comida formal. Deduciendo {Lunch} min.",
                                                    exitRecord.CheckTime, returnRecord.CheckTime, (int)absenceMinutes, shift.LunchBreakMinutes);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Checada sin par (número impar de intermedios)
                                        hasTemporaryExits = true;
                                        _logger.LogWarning("Checada intermedia huérfana ({Exit}) sin regreso registrado — posible salida sin retorno.",
                                            exitRecord.CheckTime);
                                    }
                                }

                                // Guardar resultados del análisis para aplicar después de crear el DailyAttendance
                                // (se usa una tupla local para pasar al aggregate)
                                intermediateAnalysis = (lunchMinutesDeducted, hasTemporaryExits, temporaryExitMinutes);

                                // PASO 3: Marcar TODOS los intermedios como Processed
                                foreach (var middle in middleRecords)
                                {
                                    middle.MarkAsProcessed();
                                    await _attendanceRepo.UpdateAsync(middle, cancellationToken);
                                }
                            }
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

                // 6b. Aplicar resultados del análisis de registros intermedios al aggregate
                if (intermediateAnalysis.HasValue)
                {
                    dailyAttendance.ApplyIntermediateAnalysis(
                        intermediateAnalysis.Value.Lunch,
                        intermediateAnalysis.Value.HasTempExits,
                        intermediateAnalysis.Value.TempMinutes);
                }

                // 7. Save
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
