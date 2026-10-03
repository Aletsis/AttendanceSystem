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
    private readonly IShiftRosterRepository _rosterRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISender _sender;
    private readonly ILogger<ProcessDailyAttendanceCommandHandler> _logger;

    public ProcessDailyAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IEmployeeRepository employeeRepo,
        IShiftRepository shiftRepo,
        IShiftRosterRepository rosterRepo,
        IUnitOfWork unitOfWork,
        ISender sender,
        ILogger<ProcessDailyAttendanceCommandHandler> logger)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _employeeRepo = employeeRepo;
        _shiftRepo = shiftRepo;
        _rosterRepo = rosterRepo;
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

        // 1. Obtenemos todos los empleados
        var employees = (await _employeeRepo.GetAllAsync(cancellationToken)).ToList();
        _logger.LogDebug("Obtenidos {EmployeeCount} empleados de la base de datos", employees.Count);

        // Filtrar por sucursal si se especifica
        if (request.BranchId != null)
        {
            employees = employees.Where(e => e.BranchId == request.BranchId).ToList();
            _logger.LogDebug("Filtrado por sucursal {BranchId}: {EmployeeCount} empleados", request.BranchId.Value, employees.Count);
        }

        // Filtrar por empleado si se especifica
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

        // 1.1 Carga masiva de todos los turnos
        var shifts = (await _shiftRepo.GetAllAsync(cancellationToken))
            .ToDictionary(s => s.Id);
        _logger.LogDebug("Obtenidos {ShiftCount} turnos para mapeo en memoria", shifts.Count);

        // 1.2 Carga masiva de todos los registros de asistencia diaria existentes para el rango de fechas
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

        // 1.25 Carga masiva de asignaciones de Roster de turnos para el rango de fechas
        var rosters = await _rosterRepo.GetByDateRangeAsync(
            request.StartDate,
            request.EndDate,
            request.BranchId,
            request.EmployeeId,
            cancellationToken);

        var rosterLookup = rosters
            .GroupBy(r => (r.EmployeeId.Value, r.Date.Date))
            .ToDictionary(g => g.Key, g => g.First());
        _logger.LogDebug("Obtenidos {RosterCount} registros de asignación en Roster", rosters.Count);

        // 1.3 Carga masiva de todos los registros de asistencia para el rango de fechas (incluyendo un día de buffer antes y después para turnos que cruzan el día)
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

        // 2. Iterar sobre cada día en el rango
        for (var date = request.StartDate.Date; date <= request.EndDate.Date; date = date.AddDays(1))
        {
            foreach (var employee in employees)
            {
                // Omitir si no está activo?
                if (employee.Status != EmployeeStatus.Alta) continue; // Filtrar empleados activos

                // 2.1 Limpiar el procesamiento existente para este día (Lógica de re-procesamiento)
                // Debemos liberar los AttendanceRecords para que puedan ser re-evaluados o recogidos por la lógica correcta.
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

                // Omitir si la fecha es anterior a su fecha de ingreso/alta
                if (date < employee.HireDate.Date) continue;

                // 3. Determinamos el turno y el alcance de búsqueda
                Shift? shift = null;
                bool isRestDay = false;
                bool isAutoDetected = false;
                var searchStartDate = DateOnly.FromDateTime(date);
                var searchEndDate = searchStartDate;

                // 3.1 Prioridad 1: Asignación en Roster / Calendario de turnos
                var rosterKey = (employee.Id.Value, date.Date);
                if (rosterLookup.TryGetValue(rosterKey, out var rosterEntry))
                {
                    isRestDay = rosterEntry.IsRestDay;
                    if (rosterEntry.ShiftId != null && shifts.TryGetValue(rosterEntry.ShiftId, out var rShift))
                    {
                        shift = rShift;
                    }
                }
                else
                {
                    // Determinar día de descanso predeterminado del empleado
                    if (employee.RestDay.HasValue)
                    {
                        var dayOfWeek = (WeekDay)(int)date.DayOfWeek;
                        if (employee.RestDay == dayOfWeek)
                        {
                            isRestDay = true;
                        }
                    }

                    // 3.2 Prioridad 2: Empleado con turno rotativo o sin horario fijo -> Detección automática por proximidad
                    if (employee.ShiftType == ShiftType.Rotativo || employee.ScheduleId == null)
                    {
                        var empAllRecords = recordsByEmployee.TryGetValue(employee.Id.Value, out var recs) ? recs : null;
                        var firstPunchOfDay = empAllRecords?
                            .Where(r => r.CheckTime.Date == date.Date)
                            .OrderBy(r => r.CheckTime)
                            .FirstOrDefault();

                        if (firstPunchOfDay != null)
                        {
                            var autoShift = AttendanceSystem.Domain.Services.ShiftDetectionService.FindClosestShift(
                                firstPunchOfDay.CheckTime,
                                shifts.Values);

                            if (autoShift != null)
                            {
                                shift = autoShift;
                                isAutoDetected = true;
                                _logger.LogInformation(
                                    "Turno '{ShiftName}' autodetectado por proximidad para {EmpId} en {Date} (Marcaje: {Time:HH:mm})",
                                    autoShift.Name, employee.Id.Value, date.ToString("yyyy-MM-dd"), firstPunchOfDay.CheckTime);
                            }
                        }
                    }

                    // 3.3 Prioridad 3: Turno fijo del empleado (ScheduleId)
                    if (shift == null && employee.ScheduleId != null && shifts.TryGetValue(employee.ScheduleId, out var matchedShift))
                    {
                        shift = matchedShift;
                    }
                }

                // Si es un turno nocturno o continuo, extendemos la búsqueda al día siguiente para capturar la salida
                bool isCrossDay = false;
                TimeSpan dayStartTime = TimeSpan.Zero;
                TimeSpan dayEndTime = TimeSpan.Zero;

                if (shift != null)
                {
                    dayStartTime = shift.StartTime;
                    dayEndTime = shift.EndTime;

                    if (shift.ShiftType == ShiftType.Mixto)
                    {
                        var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == date.DayOfWeek);
                        if (dayConfig != null)
                        {
                            dayStartTime = dayConfig.StartTime;
                            dayEndTime = dayConfig.EndTime;

                            // Si dayConfig es Nocturno, Continuo o Flexible, o si endTime <= startTime, cruza el día
                            if (dayEndTime <= dayStartTime || dayConfig.ShiftType == ShiftType.Nocturno || dayConfig.ShiftType == ShiftType.Continuo || dayConfig.ShiftType == ShiftType.Flexible || dayConfig.ShiftType == ShiftType.Partido || dayConfig.ShiftType == ShiftType.Rotativo)
                            {
                                isCrossDay = true;
                            }
                        }
                    }
                    else if (dayEndTime <= dayStartTime || shift.WorkHours >= TimeSpan.FromHours(24) || shift.ShiftType == ShiftType.Nocturno || shift.ShiftType == ShiftType.Continuo || shift.ShiftType == ShiftType.Flexible ||
                             (shift.ShiftType == ShiftType.Partido && shift.SecondBlockEndTime.HasValue && shift.SecondBlockStartTime.HasValue && shift.SecondBlockEndTime.Value <= shift.SecondBlockStartTime.Value))
                    {
                        isCrossDay = true;
                    }

                    if (isCrossDay)
                    {
                        searchEndDate = searchStartDate.AddDays(1);
                    }
                }

                // 4. Recuperar registros en memoria
                var employeeRecords = recordsByEmployee.TryGetValue(employee.Id.Value, out var empRecs)
                    ? empRecs
                    : new List<AttendanceRecord>();

                var startDateTime = searchStartDate.ToDateTime(TimeOnly.MinValue);
                var endDateTime = searchEndDate.ToDateTime(TimeOnly.MaxValue);

                var records = employeeRecords
                    .Where(r => r.CheckTime >= startDateTime && r.CheckTime <= endDateTime)
                    .OrderBy(r => r.CheckTime)
                    .ToList();

                // 5. Delegar el cálculo a un sub-comando específico
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
                        isRestDay,
                        isAutoDetected), cancellationToken);
                }
                else if (shift.ShiftType == ShiftType.Flexible)
                {
                    await _sender.Send(new ProcessFlexibleAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay,
                        isAutoDetected), cancellationToken);
                }
                else if (shift.ShiftType == ShiftType.Partido)
                {
                    await _sender.Send(new ProcessSplitAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay,
                        isAutoDetected), cancellationToken);
                }
                else if (shift.ShiftType == ShiftType.Nocturno || (dayEndTime <= dayStartTime && shift.ShiftType != ShiftType.Continuo && shift.ShiftType != ShiftType.Flexible && shift.ShiftType != ShiftType.Partido))
                {
                    await _sender.Send(new ProcessNightlyAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay,
                        dayStartTime,
                        dayEndTime,
                        isAutoDetected), cancellationToken);
                }
                else
                {
                    // Matutino, Vespertino o Rotativo
                    await _sender.Send(new ProcessRegularAttendanceCommand(
                        employee,
                        date,
                        shift,
                        records,
                        isRestDay,
                        isAutoDetected), cancellationToken);
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
