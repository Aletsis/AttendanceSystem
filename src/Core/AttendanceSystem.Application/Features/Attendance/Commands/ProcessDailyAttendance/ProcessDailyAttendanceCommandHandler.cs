using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Common;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

public class ProcessDailyAttendanceCommandHandler : IRequestHandler<ProcessDailyAttendanceCommand, int>
{
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IShiftRosterRepository _rosterRepo;
    private readonly ISystemConfigurationRepository _systemConfigRepo;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepo;
    private readonly IAttendanceConsolidationRepository _consolidationRepo;
    private readonly IAttendanceCalculationLockService _lockService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISender _sender;
    private readonly ILogger<ProcessDailyAttendanceCommandHandler> _logger;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;

    public ProcessDailyAttendanceCommandHandler(
        IDailyAttendanceRepository dailyRepo,
        IAttendanceRepository attendanceRepo,
        IEmployeeRepository employeeRepo,
        IShiftRepository shiftRepo,
        IShiftRosterRepository rosterRepo,
        ISystemConfigurationRepository systemConfigRepo,
        IAttendanceAdjustmentRepository adjustmentRepo,
        IAttendanceConsolidationRepository consolidationRepo,
        IAttendanceCalculationLockService lockService,
        IUnitOfWork unitOfWork,
        ISender sender,
        ILogger<ProcessDailyAttendanceCommandHandler> logger,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null)
    {
        _dailyRepo = dailyRepo;
        _attendanceRepo = attendanceRepo;
        _employeeRepo = employeeRepo;
        _shiftRepo = shiftRepo;
        _rosterRepo = rosterRepo;
        _systemConfigRepo = systemConfigRepo;
        _adjustmentRepo = adjustmentRepo;
        _consolidationRepo = consolidationRepo;
        _lockService = lockService;
        _unitOfWork = unitOfWork;
        _sender = sender;
        _logger = logger;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
    }

    public async Task<int> Handle(ProcessDailyAttendanceCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Iniciando procesamiento de asistencia diaria. Rango: {StartDate:yyyy-MM-dd} - {EndDate:yyyy-MM-dd}, BranchId: {BranchId}, EmployeeId: {EmployeeId}",
            request.StartDate,
            request.EndDate,
            request.BranchId?.Value,
            request.EmployeeId?.Value);

        // 1. Obtenemos todos los empleados base
        var allEmployees = (await _employeeRepo.GetAllAsync(cancellationToken)).ToList();

        if (request.BranchId != null)
        {
            allEmployees = allEmployees.Where(e => e.BranchId == request.BranchId).ToList();
        }

        if (request.EmployeeId != null)
        {
            allEmployees = allEmployees.Where(e => e.Id == request.EmployeeId).ToList();
        }

        // F05-P02 / H08: Identificar empleados con vigencia histórica en el rango solicitado
        // (empleados que estuvieron activos en cualquier fecha del rango, o cuya fecha de ingreso/baja solapa el periodo)
        var targetEmployees = allEmployees.Where(e =>
            e.IsActiveOn(request.StartDate) ||
            e.IsActiveOn(request.EndDate) ||
            (e.HireDate.Date <= request.EndDate.Date && (!e.TerminationDate.HasValue || e.TerminationDate.Value.Date >= request.StartDate.Date))
        ).ToList();

        var targetEmployeeIds = targetEmployees.Select(e => e.Id).ToList();

        if (!targetEmployees.Any())
        {
            _logger.LogInformation("No se encontraron empleados activos o con vigencia laboral en el periodo solicitado.");
            return 0;
        }

        // F05-P04 / H06: Exclusión distribuida ordenada por EmployeeId para garantizar ausencia de deadlocks
        return await _lockService.ExecuteWithLockAsync(targetEmployeeIds, async () =>
        {
            // F05-P04 / H06: Transacción atómica envolvente para todo el recálculo
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                int processedCount = 0;

                // 1.1 Carga masiva de todos los turnos
                var shifts = ((await _shiftRepo.GetAllAsync(cancellationToken)) ?? new List<Shift>())
                    .ToDictionary(s => s.Id);

                // 1.2 Carga masiva de asistencias diarias existentes en el rango
                var existingDailyAttendances = (await _dailyRepo.GetByDateRangeAsync(
                    request.StartDate,
                    request.EndDate,
                    request.BranchId,
                    request.EmployeeId,
                    cancellationToken)) ?? new List<DailyAttendance>();

                // 1.25 Carga masiva de asignaciones de Roster de turnos
                var rawRosters = await _rosterRepo.GetByDateRangeAsync(
                    request.StartDate,
                    request.EndDate,
                    request.BranchId,
                    request.EmployeeId,
                    cancellationToken);

                var rosters = rawRosters ?? Enumerable.Empty<ShiftRoster>();

                var rosterLookup = rosters
                    .GroupBy(r => (r.EmployeeId.Value, r.Date.Date))
                    .ToDictionary(g => g.Key, g => g.First());

                // 1.26 Carga masiva de ajustes manuales de RH (F05-P02 / H07)
                var rawAdjustments = await _adjustmentRepo.GetByDateRangeAsync(
                    request.StartDate,
                    request.EndDate,
                    request.BranchId,
                    request.EmployeeId,
                    cancellationToken);

                var adjustments = rawAdjustments ?? Enumerable.Empty<AttendanceAdjustment>();

                var adjustmentsLookup = adjustments
                    .GroupBy(a => (a.EmployeeId.Value, a.Date.Date))
                    .ToDictionary(g => g.Key, g => g.First());

                // Migración/recuperación en caliente de ajustes desde DailyAttendance existentes (F05-P02)
                foreach (var existingDA in existingDailyAttendances)
                {
                    var key = (existingDA.EmployeeId.Value, existingDA.Date.Date);
                    if (!adjustmentsLookup.ContainsKey(key))
                    {
                        if (existingDA.IsRestDayOverridden || (existingDA.HasTemporaryExits && existingDA.TemporaryExitStatus != TemporaryExitStatus.Pending))
                        {
                            var migrated = AttendanceAdjustment.Create(existingDA.EmployeeId, existingDA.Date, "Migración F05");
                            if (existingDA.IsRestDayOverridden)
                            {
                                migrated.SetRestDayOverride(existingDA.IsRestDay, "Migración F05", "Preservado de DailyAttendance");
                            }
                            if (existingDA.HasTemporaryExits && existingDA.TemporaryExitStatus != TemporaryExitStatus.Pending)
                            {
                                migrated.SetTemporaryExitClassification(existingDA.TemporaryExitStatus, existingDA.TemporaryExitNote ?? "Preservado", "Migración F05");
                            }
                            adjustmentsLookup[key] = migrated;
                            await _adjustmentRepo.AddAsync(migrated, cancellationToken);
                        }
                    }
                }

                // 1.3 Carga de marcajes biométricos (con buffer de 1 día antes y 1 día después para turnos nocturnos)
                var startQueryDate = DateOnly.FromDateTime(request.StartDate.Date.AddDays(-1));
                var endQueryDate = DateOnly.FromDateTime(request.EndDate.Date.AddDays(1));

                var processedEmployeeIds = targetEmployeeIds.ToHashSet();
                var targetEmployeesById = targetEmployees.ToDictionary(e => e.Id);

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

                // Pre-resolver zonas horarias autoritativas para los empleados a procesar
                var employeeTimeZones = new Dictionary<string, string>();
                foreach (var emp in targetEmployees)
                {
                    var tz = await _timeZoneResolver.ResolveForEmployeeAsync(emp.Id, cancellationToken);
                    employeeTimeZones[emp.Id.Value] = tz;
                }

                // 1.4 Limpieza segura del rango para empleados a procesar (solo donde el empleado estuvo activo)
                // H08: Si el empleado estaba dado de baja en esa fecha, NO eliminar su histórico
                foreach (var existingDA in existingDailyAttendances)
                {
                    if (processedEmployeeIds.Contains(existingDA.EmployeeId) &&
                        targetEmployeesById.TryGetValue(existingDA.EmployeeId, out var emp) &&
                        emp.IsActiveOn(existingDA.Date))
                    {
                        _dailyRepo.Remove(existingDA);
                    }
                }

                // H06: Poner en estado Pending únicamente los marcajes dentro de la ventana estrictamente reconstruida
                var reqStartWorkDate = DateOnly.FromDateTime(request.StartDate);
                var reqEndWorkDate = DateOnly.FromDateTime(request.EndDate);

                foreach (var record in filteredRecords)
                {
                    employeeTimeZones.TryGetValue(record.EmployeeId.Value, out var empTz);
                    var recordWorkDate = record.GetWorkDate(empTz);
                    if (recordWorkDate >= reqStartWorkDate && recordWorkDate <= reqEndWorkDate)
                    {
                        if (record.Status != AttendanceStatus.Pending)
                        {
                            record.ResetStatus();
                            await _attendanceRepo.UpdateAsync(record, cancellationToken);
                        }
                    }
                }

                // 2. Iterar sobre cada día en el rango
                for (var date = request.StartDate.Date; date <= request.EndDate.Date; date = date.AddDays(1))
                {
                    foreach (var employee in targetEmployees)
                    {
                        // H08: Omitir fechas donde el empleado no tenía vigencia laboral activa
                        if (!employee.IsActiveOn(date)) continue;

                        employeeTimeZones.TryGetValue(employee.Id.Value, out var empTimeZone);

                        var rosterKey = (employee.Id.Value, date.Date);
                        rosterLookup.TryGetValue(rosterKey, out var rosterEntry);
                        adjustmentsLookup.TryGetValue((employee.Id.Value, date.Date), out var adjustment);

                        var empAllRecords = recordsByEmployee.TryGetValue(employee.Id.Value, out var recs) ? recs : null;
                        var targetDateOnly = DateOnly.FromDateTime(date);
                        var punchesOnDate = empAllRecords?
                            .Where(r => r.GetWorkDate(empTimeZone) == targetDateOnly)
                            .Select(r => r.GetCivilTime(empTimeZone));

                        // F05-P03 / H28: Centralizar resolución de turno y descanso con precedencia estricta
                        var resolution = AttendanceShiftResolutionService.Resolve(
                            employee,
                            date,
                            rosterEntry,
                            adjustment,
                            shifts,
                            punchesOnDate);

                        Shift? shift = resolution.Shift;
                        bool isRestDay = resolution.IsRestDay;
                        bool isAutoDetected = resolution.IsAutoDetected;

                        // Determinar si cruza el día
                        bool isCrossDay = false;
                        TimeSpan dayStartTime = TimeSpan.Zero;
                        TimeSpan dayEndTime = TimeSpan.Zero;
                        var searchStartDate = DateOnly.FromDateTime(date);
                        var searchEndDate = searchStartDate;

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

                                    if (dayEndTime <= dayStartTime || dayConfig.ShiftType == ShiftType.Nocturno ||
                                        dayConfig.ShiftType == ShiftType.Continuo || dayConfig.ShiftType == ShiftType.Flexible ||
                                        dayConfig.ShiftType == ShiftType.Partido || dayConfig.ShiftType == ShiftType.Rotativo)
                                    {
                                        isCrossDay = true;
                                    }
                                }
                            }
                            else if (dayEndTime <= dayStartTime || shift.WorkHours >= TimeSpan.FromHours(24) ||
                                     shift.ShiftType == ShiftType.Nocturno || shift.ShiftType == ShiftType.Continuo ||
                                     shift.ShiftType == ShiftType.Flexible || shift.ShiftType == ShiftType.Partido)
                            {
                                isCrossDay = true;
                            }

                            if (isCrossDay)
                            {
                                searchEndDate = searchStartDate.AddDays(1);
                            }
                        }

                        // Recuperar registros biométricos del día
                        var employeeRecords = recordsByEmployee.TryGetValue(employee.Id.Value, out var empRecsList)
                            ? empRecsList
                            : new List<AttendanceRecord>();

                        var records = employeeRecords
                            .Where(r => {
                                var wd = r.GetWorkDate(empTimeZone);
                                return wd >= searchStartDate && wd <= searchEndDate;
                            })
                            .OrderBy(r => r.CheckTime)
                            .ToList();

                        // Delegar a subcomandos pasando Adjustment (H07) y TimeZoneId
                        if (shift == null)
                        {
                            await _sender.Send(new ProcessNoShiftAttendanceCommand(
                                employee, date, records, isRestDay, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }
                        else if (shift.ShiftType == ShiftType.Mixto)
                        {
                            await _sender.Send(new ProcessMixAttendanceCommand(
                                employee, date, shift, records, isRestDay, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }
                        else if (shift.ShiftType == ShiftType.Continuo)
                        {
                            await _sender.Send(new ProcessContinuousAttendanceCommand(
                                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }
                        else if (shift.ShiftType == ShiftType.Flexible)
                        {
                            await _sender.Send(new ProcessFlexibleAttendanceCommand(
                                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }
                        else if (shift.ShiftType == ShiftType.Partido)
                        {
                            await _sender.Send(new ProcessSplitAttendanceCommand(
                                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }
                        else if (shift.ShiftType == ShiftType.Nocturno || (dayEndTime <= dayStartTime && shift.ShiftType != ShiftType.Continuo && shift.ShiftType != ShiftType.Flexible && shift.ShiftType != ShiftType.Partido))
                        {
                            await _sender.Send(new ProcessNightlyAttendanceCommand(
                                employee, date, shift, records, isRestDay, dayStartTime, dayEndTime, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }
                        else
                        {
                            await _sender.Send(new ProcessRegularAttendanceCommand(
                                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone), cancellationToken);
                        }

                        processedCount++;
                    }
                }

                // Persistir los registros diarios generados dentro de la transacción
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // F05-P05 / H18: Recalcular horas extras semanales expandiendo hasta el fin de semana de EndDate
                var systemConfig = await _systemConfigRepo.GetConfigurationAsync(cancellationToken);
                var weeklyStartDay = systemConfig?.WeeklyStartDay ?? DayOfWeek.Monday;
                var earliestWeekStart = WeeklyHoursCalculationHelper.GetWeekStart(request.StartDate, weeklyStartDay);
                var latestWeekEnd = WeeklyHoursCalculationHelper.GetWeekEnd(request.EndDate, weeklyStartDay);

                bool hasSubsequentUpdates = false;
                foreach (var employee in targetEmployees)
                {
                    var empAttendances = (await _dailyRepo.GetByEmployeeAndDateRangeAsync(
                        employee.Id,
                        earliestWeekStart,
                        latestWeekEnd,
                        cancellationToken)) ?? new List<DailyAttendance>();

                    if (empAttendances.Any(a => a.WeeklyWorkHours.HasValue && a.WeeklyWorkHours.Value > TimeSpan.Zero))
                    {
                        var overtimeMap = WeeklyHoursCalculationHelper.CalculateEffectiveOvertime(
                            empAttendances,
                            employee,
                            weeklyStartDay);

                        foreach (var att in empAttendances)
                        {
                            if (overtimeMap.TryGetValue(att.Id, out var effectiveOt))
                            {
                                if (att.OvertimeMinutes != effectiveOt)
                                {
                                    att.SetCalculatedOvertime(effectiveOt);
                                    _dailyRepo.Update(att);
                                    hasSubsequentUpdates = true;
                                }
                            }
                        }
                    }
                }

                // F05-P06 / H27: Completar solicitudes de consolidación pendientes para los empleados procesados
                foreach (var employee in targetEmployees)
                {
                    var pendingReq = await _consolidationRepo.GetPendingByEmployeeAsync(employee.Id, cancellationToken);
                    if (pendingReq != null && pendingReq.StartDate >= request.StartDate.Date && pendingReq.EndDate <= request.EndDate.Date)
                    {
                        pendingReq.MarkAsCompleted();
                        _consolidationRepo.Update(pendingReq);
                        hasSubsequentUpdates = true;
                    }
                }

                if (hasSubsequentUpdates)
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                // Commit atómico final
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation(
                    "Procesamiento atómico de asistencia diaria completado. Registros procesados: {ProcessedCount}",
                    processedCount);

                return processedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante el recálculo de asistencia diaria. Ejecutando rollback atómico.");
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }, cancellationToken);
    }
}
