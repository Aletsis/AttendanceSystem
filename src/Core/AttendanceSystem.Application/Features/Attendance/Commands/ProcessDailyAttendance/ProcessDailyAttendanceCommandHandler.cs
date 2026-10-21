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
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Common;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;

public class ProcessDailyAttendanceCommandHandler : IRequestHandler<ProcessDailyAttendanceCommand, int>
{
    /// <summary>
    /// Tamaño del bloque de datos que se carga cuando la unidad se extiende más allá de lo cargado.
    /// Es sólo una granularidad de lectura: la extensión la decide la dependencia real entre jornadas.
    /// </summary>
    private const int ExtensionLoadChunkDays = 7;

    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IShiftRosterRepository _rosterRepo;
    private readonly ISystemConfigurationRepository _systemConfigRepo;
    private readonly IAttendanceAdjustmentRepository _adjustmentRepo;
    private readonly IPunchAssignmentRepository _assignmentRepo;
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
        IPunchAssignmentRepository assignmentRepo,
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
        _assignmentRepo = assignmentRepo;
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
        // F02-05: orden estable por identificador para que el resultado no dependa del orden de lectura.
        // F06-08: con períodos de empleo (reingresos), sólo se procesan los días cubiertos por alguno.
        var targetEmployees = allEmployees.Where(e => e.IsActiveDuring(request.StartDate, request.EndDate))
            .OrderBy(e => e.Id.Value, StringComparer.Ordinal).ToList();


        if (!targetEmployees.Any())
        {
            _logger.LogInformation("No se encontraron empleados activos o con vigencia laboral en el periodo solicitado.");
            return 0;
        }

        var requestStart = request.StartDate.Date;
        var requestEnd = request.EndDate.Date;

        // Catálogos de sólo lectura compartidos por todas las unidades.
        var shifts = ((await _shiftRepo.GetAllAsync(cancellationToken)) ?? new List<Shift>())
            .ToDictionary(s => s.Id);
        var systemConfig = await _systemConfigRepo.GetConfigurationAsync(cancellationToken);
        var weeklyStartDay = systemConfig?.WeeklyStartDay ?? DayOfWeek.Monday;
        var assignedAt = DateTime.UtcNow;

        // F03-04: la unidad atómica es (empleado, ventana de dependencias). Cada una corre en su propia
        // transacción con el bloqueo PostgreSQL del empleado (F03-02); un fallo revierte sólo su unidad.
        int processedCount = 0;
        int successfulUnits = 0;
        var failures = new List<(AttendanceRecalculationUnitFailure Failure, Exception Exception)>();

        foreach (var employee in targetEmployees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                processedCount += await ExclusiveAttendanceWrite.RunAsync(
                    _unitOfWork,
                    _lockService,
                    new[] { employee.Id },
                    () => RecalculateUnitAsync(employee, requestStart, requestEnd, shifts, weeklyStartDay, systemConfig, assignedAt, cancellationToken),
                    _ => true,
                    cancellationToken);
                successfulUnits++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Recálculo cancelado: {Successful} de {Total} empleados confirmados; la unidad en curso se revirtió.",
                    successfulUnits, targetEmployees.Count);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en la unidad de recálculo del empleado {EmployeeId}. Se revirtió solo esa unidad.", employee.Id.Value);
                failures.Add((new AttendanceRecalculationUnitFailure(employee.Id.Value, ex.GetType().Name, ex.Message), ex));
            }
        }

        if (failures.Count > 0)
        {
            if (targetEmployees.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0].Exception).Throw();
            }

            throw new AttendanceRecalculationException(
                targetEmployees.Count,
                successfulUnits,
                failures.Select(f => f.Failure).ToList(),
                failures[0].Exception);
        }

        _logger.LogInformation(
            "Procesamiento de asistencia diaria completado. Unidades: {Units}. Registros procesados: {ProcessedCount}",
            successfulUnits, processedCount);

        return processedCount;
    }

    /// <summary>
    /// Una unidad atómica: carga (ya bajo bloqueo), reconstruye y recalcula la semana de extras del empleado.
    /// Se ejecuta dentro de la transacción de la unidad.
    /// </summary>
    private async Task<int> RecalculateUnitAsync(
        Employee employee,
        DateTime requestStart,
        DateTime requestEnd,
        IReadOnlyDictionary<ShiftId, Shift> shifts,
        DayOfWeek weeklyStartDay,
        SystemConfiguration? systemConfig,
        DateTime assignedAt,
        CancellationToken cancellationToken)
    {
        var tz = await _timeZoneResolver.ResolveForEmployeeAsync(employee.Id, cancellationToken);
        var ws = new EmployeeRecalculationWorkset(employee, tz) { Lunch = systemConfig?.LunchPolicy ?? LunchPolicy.Default };
        var worksets = new Dictionary<EmployeeId, EmployeeRecalculationWorkset> { [employee.Id] = ws };

        // Lectura posterior al bloqueo: ningún otro escritor puede cambiar estos datos hasta el commit.
        await LoadAsync(worksets, requestStart.AddDays(-1), requestEnd.AddDays(1), null, employee.Id, cancellationToken);

        var processed = await RecalculateEmployeeAsync(ws, requestStart, requestEnd, shifts, assignedAt, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // F06-06 / F06-07: extras efectivas de todas las jornadas que dependen de la unidad: las semanas que
        // la contienen (bolsa semanal) y, con tope de período, el período contable completo. Se recalculan
        // siempre (no sólo con bolsa semanal) para que autorización diaria, redondeo y topes sean uniformes.
        bool hasSubsequentUpdates = false;
        var unitStart = ws.UnitDates.Count > 0 ? ws.UnitDates.Min : requestStart;
        var unitEnd = ws.UnitDates.Count > 0 ? ws.UnitDates.Max : requestEnd;
        var first = unitStart < requestStart ? unitStart : requestStart;
        var last = unitEnd > requestEnd ? unitEnd : requestEnd;
        var earliest = WeeklyHoursCalculationHelper.GetWeekStart(first, weeklyStartDay);
        var latest = WeeklyHoursCalculationHelper.GetWeekEnd(last, weeklyStartDay);
        Func<DateTime, DateTime> periodStartOf = date => WorkPeriodCalculator.Containing(date, systemConfig).Start;
        if (employee.OvertimeCapType == OvertimeCapType.Period)
        {
            var firstPeriod = WorkPeriodCalculator.Containing(first, systemConfig);
            var lastPeriod = WorkPeriodCalculator.Containing(last, systemConfig);
            if (firstPeriod.Start < earliest) earliest = firstPeriod.Start;
            if (lastPeriod.End > latest) latest = lastPeriod.End;
        }

        var empAttendances = (await _dailyRepo.GetByEmployeeAndDateRangeAsync(
            employee.Id,
            earliest,
            latest,
            cancellationToken)) ?? new List<DailyAttendance>();

        var overtime = WeeklyHoursCalculationHelper.Calculate(empAttendances, employee, weeklyStartDay, periodStartOf);
        foreach (var att in empAttendances)
        {
            if (overtime.TryGetValue(att.Id, out var ot) && (att.OvertimeMinutes != ot.Effective || att.RawOvertimeMinutes != ot.Raw))
            {
                att.SetCalculatedOvertime(ot.Raw, ot.Effective);
                _dailyRepo.Update(att);
                hasSubsequentUpdates = true;
            }
        }

        // Las solicitudes de consolidación no se completan aquí: sólo su consumidor lo hace, comprobando la
        // versión capturada al reclamarlas, para no descartar ampliaciones concurrentes (F04-06 / A07).

        if (hasSubsequentUpdates)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return processed;
    }

    /// <summary>
    /// Reconstruye la unidad de un empleado: rango solicitado más las jornadas vecinas que dependen de él.
    /// </summary>
    private async Task<int> RecalculateEmployeeAsync(
        EmployeeRecalculationWorkset ws,
        DateTime requestStart,
        DateTime requestEnd,
        IReadOnlyDictionary<ShiftId, Shift> shifts,
        DateTime assignedAt,
        CancellationToken cancellationToken)
    {
        var employee = ws.Employee;
        int processed = 0;

        // F02-03 (hacia atrás): la jornada previa se reconstruye sólo si su turno real alcanza un
        // marcaje libre del primer día solicitado; así su salida no queda libre para el día siguiente.
        var unitStart = requestStart;
        var previous = requestStart.AddDays(-1);
        if (employee.IsActiveOn(previous) && PreviousJornadaReachesFreePunch(ws, previous, shifts))
        {
            unitStart = previous;
            _logger.LogDebug(
                "Empleado {EmpId}: se incluye {Date:yyyy-MM-dd} en la unidad porque su jornada puede consumir marcajes de {Next:yyyy-MM-dd}.",
                employee.Id.Value, previous, requestStart);
        }

        var date = unitStart;
        var lastDate = requestEnd;
        while (true)
        {
            if (date <= lastDate)
            {
                if (employee.IsActiveOn(date))
                {
                    await EnsureLoadedAsync(ws, date, cancellationToken);
                    await IncludeInUnitAsync(ws, date, cancellationToken);
                    await RecalculateJornadaAsync(ws, date, shifts, assignedAt, cancellationToken);
                    processed++;
                }

                date = date.AddDays(1);
                continue;
            }

            // F02-03 (hacia adelante): si la unidad cambió qué marcajes del día siguiente consume,
            // la jornada siguiente ya calculada depende de este resultado y se reconstruye también.
            var next = lastDate.AddDays(1);
            if (ws.UnitDates.Count == 0 || !employee.IsActiveOn(next)) break;

            await EnsureLoadedAsync(ws, next, cancellationToken);
            var (before, after) = ws.BoundaryClaims(DateOnly.FromDateTime(next));
            if (before.SetEquals(after) || !ws.HasPersistedJornada(next)) break;

            _logger.LogInformation(
                "Empleado {EmpId}: la reconstrucción cambió los marcajes consumidos de {Next:yyyy-MM-dd}; se extiende la unidad.",
                employee.Id.Value, next);
            lastDate = next;
        }

        RemoveReleasedAssignments(ws);
        await ReleaseOrphanedClaimsAsync(ws, cancellationToken);

        return processed;
    }

    /// <summary>
    /// Elimina las filas de jornadas de la unidad cuyo marcaje ya no consume ninguna jornada.
    /// </summary>
    private void RemoveReleasedAssignments(EmployeeRecalculationWorkset ws)
    {
        foreach (var stored in ws.StoredAssignments.Values
                     .Where(a => ws.IsInUnit(a.WorkDate) && !ws.NewAssignments.ContainsKey(a.AttendanceRecordId))
                     .ToList())
        {
            _assignmentRepo.Remove(stored);
            ws.StoredAssignments.Remove(stored.AttendanceRecordId);
        }
    }

    /// <summary>
    /// Marcajes que la unidad tenía asignados (o que caen en sus fechas) y que ya no consume ninguna
    /// jornada: vuelven a ser candidatos libres. Las reclamaciones de jornadas externas no se tocan.
    /// </summary>
    private async Task ReleaseOrphanedClaimsAsync(EmployeeRecalculationWorkset ws, CancellationToken cancellationToken)
    {
        if (ws.UnitDates.Count == 0) return;

        var unitStart = DateOnly.FromDateTime(ws.UnitDates.Min);
        var unitEnd = DateOnly.FromDateTime(ws.UnitDates.Max);
        var persisted = ws.PersistedClaims();

        foreach (var record in PunchAssignmentPolicy.Chronological(ws.Records.Values))
        {
            if (ws.NewAssignments.ContainsKey(record.Id) || record.Status == AttendanceStatus.Pending) continue;

            bool claimedByUnit = persisted.TryGetValue(record.Id, out var claim) && ws.IsInUnit(claim.Owner);
            bool claimedOutside = persisted.ContainsKey(record.Id) && !claimedByUnit;
            var workDate = ws.WorkDateOf(record);
            bool inUnitDates = workDate >= unitStart && workDate <= unitEnd;

            if (claimedByUnit || (inUnitDates && !claimedOutside))
            {
                record.ResetStatus();
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }
    }

    private async Task IncludeInUnitAsync(EmployeeRecalculationWorkset ws, DateTime date, CancellationToken cancellationToken)
    {
        if (!ws.UnitDates.Add(date.Date)) return;

        if (!ws.ExistingDaily.TryGetValue(date.Date, out var existingDA)) return;

        // Migración/recuperación en caliente de ajustes desde DailyAttendance existentes (F05-P02)
        if (!ws.Adjustments.ContainsKey(date.Date) &&
            (existingDA.IsRestDayOverridden || (existingDA.HasTemporaryExits && existingDA.TemporaryExitStatus != TemporaryExitStatus.Pending)))
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
            ws.Adjustments[date.Date] = migrated;
            await _adjustmentRepo.AddAsync(migrated, cancellationToken);
        }

        // F02-06: la jornada se reemplaza (nuevo identificador); los consumidores la referencian por (EmployeeId, Date).
        _dailyRepo.Remove(existingDA);
    }

    private sealed record JornadaPlan(
        ShiftResolutionResult Resolution,
        AttendanceAdjustment? Adjustment,
        TimeSpan DayStartTime,
        TimeSpan DayEndTime,
        DateOnly SearchStartDate,
        DateOnly SearchEndDate);

    private JornadaPlan ResolvePlan(EmployeeRecalculationWorkset ws, DateTime date, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var workDate = DateOnly.FromDateTime(date);
        ws.Rosters.TryGetValue(date.Date, out var rosterEntry);
        ws.Adjustments.TryGetValue(date.Date, out var adjustment);

        var unavailable = ws.Unavailable(workDate, includeOwnManual: true);
        var punchesOnDate = ws.Records.Values
            .Where(r => ws.WorkDateOf(r) == workDate && !unavailable.ContainsKey(r.Id))
            .Select(r => r.GetCivilTime(ws.TimeZoneId))
            .ToList();

        // F05-P03 / H28: Centralizar resolución de turno y descanso con precedencia estricta
        var resolution = AttendanceShiftResolutionService.Resolve(
            ws.Employee,
            date,
            rosterEntry,
            adjustment,
            shifts,
            punchesOnDate);

        var shift = resolution.Shift;
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
        }

        return new JornadaPlan(
            resolution,
            adjustment,
            dayStartTime,
            dayEndTime,
            workDate,
            isCrossDay ? workDate.AddDays(1) : workDate);
    }

    private bool PreviousJornadaReachesFreePunch(EmployeeRecalculationWorkset ws, DateTime previous, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var plan = ResolvePlan(ws, previous, shifts);
        var reach = NextDayReach(ws, plan, previous);
        if (reach == null) return false;

        var next = DateOnly.FromDateTime(previous.AddDays(1));
        var unavailable = ws.Unavailable(DateOnly.FromDateTime(previous), includeOwnManual: false);
        return ws.Records.Values.Any(r =>
            ws.WorkDateOf(r) == next &&
            !unavailable.ContainsKey(r.Id) &&
            r.GetCivilTime(ws.TimeZoneId) <= reach.Value);
    }

    /// <summary>
    /// Último instante civil del día siguiente que el procesador de la jornada puede consumir, según
    /// el turno real y las ventanas de cada procesador (F02-03). <c>null</c> si sólo usa su propio día.
    /// Debe ser un límite superior de lo que el procesador realmente toma.
    /// </summary>
    private static DateTime? NextDayReach(EmployeeRecalculationWorkset ws, JornadaPlan plan, DateTime date)
    {
        var shift = plan.Resolution.Shift;
        if (shift == null || plan.SearchEndDate == plan.SearchStartDate) return null;

        var effectiveType = shift.ShiftType;
        if (shift.ShiftType == ShiftType.Mixto)
        {
            var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == date.DayOfWeek);
            if (dayConfig == null) return null;
            effectiveType = dayConfig.ShiftType;
        }

        bool routesToNightly = effectiveType == ShiftType.Nocturno ||
            (plan.DayEndTime <= plan.DayStartTime &&
             effectiveType != ShiftType.Continuo && effectiveType != ShiftType.Flexible && effectiveType != ShiftType.Partido);

        if (routesToNightly)
        {
            // Ventana de salida del procesador nocturno: salida programada (día siguiente) ± 7 h.
            return date.Date.Add(plan.DayEndTime).AddDays(1).AddHours(7);
        }

        if (effectiveType == ShiftType.Partido && shift.ShiftType == ShiftType.Partido)
        {
            if (!shift.SecondBlockStartTime.HasValue || !shift.SecondBlockEndTime.HasValue) return null;

            // Mismo corte del procesador partido para el bloque 2.
            var schedB2Out = shift.SecondBlockEndTime.Value <= shift.SecondBlockStartTime.Value
                ? date.Date.AddDays(1).Add(shift.SecondBlockEndTime.Value)
                : date.Date.Add(shift.SecondBlockEndTime.Value);
            var maxB2CutoffTomorrow = date.Date.AddDays(1).Add(shift.StartTime).AddHours(-2.5);
            var maxB2EndBySched = schedB2Out.AddHours(7);
            return maxB2CutoffTomorrow > schedB2Out
                ? (maxB2EndBySched < maxB2CutoffTomorrow ? maxB2EndBySched : maxB2CutoffTomorrow)
                : maxB2EndBySched;
        }

        if (effectiveType == ShiftType.Continuo || effectiveType == ShiftType.Flexible)
        {
            if (shift.PunchTrackingMode == PunchTrackingMode.MultiInterval) return null;

            // F06-01: continuo toma el último marcaje de su ventana; flexible sólo busca en el día siguiente
            // cuando la entrada es el único marcaje libre de su día. Ambos con el tope de la política de ventanas.
            var unavailable = ws.Unavailable(DateOnly.FromDateTime(date), includeOwnManual: true);
            var ownDay = PunchAssignmentPolicy.Chronological(ws.Records.Values
                .Where(r => ws.WorkDateOf(r) == plan.SearchStartDate && !unavailable.ContainsKey(r.Id)))
                .ToList();
            if (ownDay.Count == 0) return null;
            if (effectiveType == ShiftType.Flexible && ownDay.Count != 1) return null;
            var effective = shift.ShiftType == ShiftType.Mixto
                ? shift.EffectiveFor(shift.Days.First(d => d.DayOfWeek == date.DayOfWeek))
                : shift;
            return JornadaWindowPolicy.ExitWindowEnd(
                ownDay[0].GetCivilTime(ws.TimeZoneId),
                JornadaWindowPolicy.ScheduledStart(date, effective.StartTime, effective.HasEntryWindow),
                effective.WorkHours,
                null);
        }

        // Regular y sin turno sólo consumen marcajes de su propia fecha laboral.
        return null;
    }

    /// <summary>
    /// F06-01: inicio civil de la ventana de entrada de la jornada siguiente si es laborable (con turno y sin
    /// descanso); una salida faltante de hoy no puede consumir esa entrada.
    /// </summary>
    private DateTime? NextJornadaEntryStart(EmployeeRecalculationWorkset ws, DateTime date, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var next = date.AddDays(1);
        if (!ws.Employee.IsActiveOn(next) || next > ws.LoadedThrough) return null;

        var plan = ResolvePlan(ws, next, shifts);
        if (plan.Resolution.Shift == null || plan.Resolution.IsRestDay) return null;
        return JornadaWindowPolicy.EntryWindowStart(next.Date.Add(plan.DayStartTime));
    }

    /// <summary>
    /// Calcula una jornada sobre los marcajes libres, valida sus invariantes y registra sus asignaciones.
    /// </summary>
    private async Task RecalculateJornadaAsync(
        EmployeeRecalculationWorkset ws,
        DateTime date,
        IReadOnlyDictionary<ShiftId, Shift> shifts,
        DateTime assignedAt,
        CancellationToken cancellationToken)
    {
        var employee = ws.Employee;
        var workDate = DateOnly.FromDateTime(date);
        var plan = ResolvePlan(ws, date, shifts);
        var shift = plan.Resolution.Shift;
        var isRestDay = plan.Resolution.IsRestDay;
        var isAutoDetected = plan.Resolution.IsAutoDetected;
        var adjustment = plan.Adjustment;
        var empTimeZone = ws.TimeZoneId;

        // F02-04: el estado Pending/Processed deja de ser una entrada heredada; se deriva de la pertenencia.
        var unavailable = ws.Unavailable(workDate, includeOwnManual: true);
        var windowRecords = PunchAssignmentPolicy.Chronological(ws.Records.Values
            .Where(r =>
            {
                var wd = ws.WorkDateOf(r);
                return wd >= plan.SearchStartDate && wd <= plan.SearchEndDate;
            }))
            .ToList();

        var records = new List<AttendanceRecord>();
        var originalState = new Dictionary<AttendanceRecordId, (AttendanceStatus Status, CheckType Type)>();
        foreach (var record in windowRecords)
        {
            if (unavailable.ContainsKey(record.Id)) continue;
            originalState[record.Id] = (record.Status, record.CheckType);
            if (record.Status != AttendanceStatus.Pending)
            {
                record.ResetStatus();
            }
            records.Add(record);
        }

        var draft = new PunchAssignmentDraft();
        var nextEntryStart = plan.SearchEndDate > plan.SearchStartDate ? NextJornadaEntryStart(ws, date, shifts) : null;

        // Delegar a subcomandos pasando Adjustment (H07), TimeZoneId y el borrador de asignaciones (F02-06)
        if (shift == null)
        {
            await _sender.Send(new ProcessNoShiftAttendanceCommand(
                employee, date, records, isRestDay, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Mixto)
        {
            await _sender.Send(new ProcessMixAttendanceCommand(
                employee, date, shift, records, isRestDay, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, NextJornadaEntryStart: nextEntryStart, Lunch: ws.Lunch), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Continuo)
        {
            await _sender.Send(new ProcessContinuousAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, NextJornadaEntryStart: nextEntryStart, Lunch: ws.Lunch), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Flexible)
        {
            await _sender.Send(new ProcessFlexibleAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, NextJornadaEntryStart: nextEntryStart, Lunch: ws.Lunch), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Partido)
        {
            await _sender.Send(new ProcessSplitAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Nocturno || (plan.DayEndTime <= plan.DayStartTime && shift.ShiftType != ShiftType.Continuo && shift.ShiftType != ShiftType.Flexible && shift.ShiftType != ShiftType.Partido))
        {
            await _sender.Send(new ProcessNightlyAttendanceCommand(
                employee, date, shift, records, isRestDay, plan.DayStartTime, plan.DayEndTime, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft), cancellationToken);
        }
        else
        {
            await _sender.Send(new ProcessRegularAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, Lunch: ws.Lunch), cancellationToken);
        }

        // F02-02 / F02-06: validar el resultado completo antes de aceptarlo; un fallo revierte toda la unidad.
        var proposed = draft.BuildAssignments(adjustment, assignedAt);
        if (proposed.Count > 0)
        {
            await EnsureKnownAsync(ws, proposed.Select(a => a.AttendanceRecordId), cancellationToken);
            PunchAssignmentPolicy.EnsureValid(
                employee.Id,
                workDate,
                proposed,
                ws.KnownRecords,
                ws.Unavailable(workDate, includeOwnManual: false));
        }

        foreach (var assignment in proposed)
        {
            ws.NewAssignments[assignment.AttendanceRecordId] = assignment;
        }

        // Estado derivado: asignado → Processed con el tipo de su posición; candidato no consumido → Pending.
        var assignedById = proposed.ToDictionary(a => a.AttendanceRecordId);
        var manualOutsideWindow = proposed
            .Where(a => !originalState.ContainsKey(a.AttendanceRecordId))
            .Select(a => ws.KnownRecords[a.AttendanceRecordId])
            .ToList();
        foreach (var record in manualOutsideWindow)
        {
            originalState[record.Id] = (record.Status, record.CheckType);
        }

        // Candidatos que una jornada externa posterior ya tenía y que esta jornada no tomó: siguen siendo suyos.
        var persisted = ws.PersistedClaims();

        foreach (var record in records.Concat(manualOutsideWindow))
        {
            if (assignedById.TryGetValue(record.Id, out var assignment))
            {
                record.MarkAsProcessed();
                var inferred = PunchAssignmentPolicy.InferredCheckType(assignment.Role);
                if (inferred != null) record.SetInferredType(inferred);
            }
            else if (persisted.TryGetValue(record.Id, out var claim) && claim.Owner != workDate && !ws.IsInUnit(claim.Owner))
            {
                var (originalStatus, originalType) = originalState[record.Id];
                if (originalStatus == AttendanceStatus.Pending) record.ResetStatus(); else record.MarkAsProcessed();
                record.SetInferredType(originalType);
            }
            else if (record.Status != AttendanceStatus.Pending)
            {
                record.ResetStatus();
            }

            var (status, type) = originalState[record.Id];
            if (record.Status != status || record.CheckType != type)
            {
                await _attendanceRepo.UpdateAsync(record, cancellationToken);
            }
        }

        SynchronizeAssignments(ws, workDate);
    }

    /// <summary>
    /// Inserta o actualiza en sitio las filas de asignación de la jornada. Los borrados se difieren a
    /// <see cref="RemoveReleasedAssignments"/>: un marcaje que deja una jornada puede tomarlo otra
    /// posterior de la unidad, y su fila debe actualizarse (no borrarse e insertarse con la misma clave).
    /// </summary>
    private void SynchronizeAssignments(EmployeeRecalculationWorkset ws, DateOnly workDate)
    {
        var desired = ws.NewAssignments.Values.Where(a => a.WorkDate == workDate).ToList();

        foreach (var assignment in desired)
        {
            if (ws.StoredAssignments.TryGetValue(assignment.AttendanceRecordId, out var stored))
            {
                if (!stored.IsEquivalentTo(assignment))
                {
                    stored.Reassign(assignment.WorkDate, assignment.Role, assignment.Origin, assignment.AssignedAt);
                    _assignmentRepo.Update(stored);
                }
            }
            else
            {
                _assignmentRepo.Add(assignment);
                ws.StoredAssignments[assignment.AttendanceRecordId] = assignment;
            }
        }
    }

    private async Task EnsureKnownAsync(EmployeeRecalculationWorkset ws, IEnumerable<AttendanceRecordId> recordIds, CancellationToken cancellationToken)
    {
        foreach (var recordId in recordIds)
        {
            if (ws.KnownRecords.ContainsKey(recordId)) continue;
            var record = await _attendanceRepo.GetByIdAsync(recordId, cancellationToken);
            if (record != null) ws.KnownRecords[record.Id] = record;
        }
    }

    private async Task EnsureLoadedAsync(EmployeeRecalculationWorkset ws, DateTime date, CancellationToken cancellationToken)
    {
        if (date <= ws.LoadedThrough) return;

        var from = ws.LoadedThrough.AddDays(1);
        var to = date.AddDays(ExtensionLoadChunkDays - 1);
        await LoadAsync(
            new Dictionary<EmployeeId, EmployeeRecalculationWorkset> { [ws.Employee.Id] = ws },
            from, to, null, ws.Employee.Id, cancellationToken);
    }

    /// <summary>
    /// Carga los datos necesarios para calcular las jornadas [from, to]: sus rosters y ajustes, los marcajes
    /// que pueden consumir y las reclamaciones de las jornadas vecinas que compiten por ellos.
    /// </summary>
    private async Task LoadAsync(
        IReadOnlyDictionary<EmployeeId, EmployeeRecalculationWorkset> worksets,
        DateTime from,
        DateTime to,
        BranchId? branchId,
        EmployeeId? employeeId,
        CancellationToken cancellationToken)
    {
        var claimFrom = from.AddDays(-1);
        var claimTo = to.AddDays(1);

        var dailies = (await _dailyRepo.GetByDateRangeAsync(claimFrom, claimTo, branchId, employeeId, cancellationToken)) ?? new List<DailyAttendance>();
        foreach (var daily in dailies)
        {
            if (daily.Date.Date < claimFrom || daily.Date.Date > claimTo) continue;
            if (worksets.TryGetValue(daily.EmployeeId, out var ws)) ws.ExistingDaily.TryAdd(daily.Date.Date, daily);
        }

        var rosters = (await _rosterRepo.GetByDateRangeAsync(from, to, branchId, employeeId, cancellationToken)) ?? Enumerable.Empty<ShiftRoster>();
        foreach (var roster in rosters)
        {
            if (roster.Date.Date < from || roster.Date.Date > to) continue;
            if (worksets.TryGetValue(roster.EmployeeId, out var ws)) ws.Rosters.TryAdd(roster.Date.Date, roster);
        }

        var adjustments = (await _adjustmentRepo.GetByDateRangeAsync(claimFrom, claimTo, branchId, employeeId, cancellationToken)) ?? Enumerable.Empty<AttendanceAdjustment>();
        foreach (var adjustment in adjustments)
        {
            if (adjustment.Date.Date < claimFrom || adjustment.Date.Date > claimTo) continue;
            if (worksets.TryGetValue(adjustment.EmployeeId, out var ws)) ws.Adjustments.TryAdd(adjustment.Date.Date, adjustment);
        }

        var records = (await _attendanceRepo.GetByDateRangeAsync(
            DateOnly.FromDateTime(from),
            DateOnly.FromDateTime(claimTo),
            employeeId,
            cancellationToken)) ?? new List<AttendanceRecord>();
        foreach (var record in records)
        {
            if (worksets.TryGetValue(record.EmployeeId, out var ws)) ws.AddRecord(record);
        }

        foreach (var ws in worksets.Values)
        {
            var byJornada = (await _assignmentRepo.GetByEmployeeAndWorkDateRangeAsync(
                ws.Employee.Id,
                DateOnly.FromDateTime(claimFrom),
                DateOnly.FromDateTime(claimTo),
                cancellationToken)) ?? new List<PunchAssignment>();

            var byRecord = ws.Records.Count == 0
                ? new List<PunchAssignment>()
                : (await _assignmentRepo.GetByRecordIdsAsync(ws.Records.Keys.ToList(), cancellationToken)) ?? new List<PunchAssignment>();

            foreach (var assignment in byJornada.Concat(byRecord))
            {
                ws.TrackLoaded(assignment);
            }

            // Marcajes referenciados manualmente: se cargan para validar propietario y fecha (H07).
            await EnsureKnownAsync(ws, ws.Adjustments.Values.SelectMany(EmployeeRecalculationWorkset.ManualRecordIds), cancellationToken);

            if (to > ws.LoadedThrough) ws.LoadedThrough = to;
        }
    }
}
