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

    /// <summary>
    /// F07-04: empleados por transacción. El lote comparte bloqueos (un viaje), lecturas y guardado; si falla, se
    /// reprocesa empleado por empleado para que un fallo sólo revierta su propia unidad (F03-04).
    /// </summary>
    public const int DefaultBatchSize = 25;

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
    private readonly IAttendanceCalculationReader _reader;
    private readonly int _batchSize;
    private readonly Dictionary<BranchId, string> _branchTimeZones = new();
    private readonly AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? _closures;
    /// <summary>Caso 17: días de periodos cerrados; no se recalculan ni se eliminan.</summary>
    private Func<DateTime, bool> _isClosed = _ => false;

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
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        IAttendanceCalculationReader? reader = null,
        int batchSize = DefaultBatchSize,
        AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? closures = null)
    {
        _closures = closures;
        _reader = reader ?? new RepositoryCalculationReader(employeeRepo, dailyRepo, rosterRepo, adjustmentRepo, attendanceRepo, assignmentRepo);
        _batchSize = Math.Max(1, batchSize);
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

        // F07-02 / A05: el rango se valida antes de consultar o modificar nada (también si se invoca sin MediatR).
        var rangeError = DateRangePolicy.Validate(request.StartDate, request.EndDate, DateRangePolicy.MaxRecalculationDays);
        if (rangeError != null) throw new AttendanceSystem.Application.Exceptions.InvalidDateRangeException(rangeError);

        if (_closures != null) _isClosed = await _closures.ClosedDaysAsync(cancellationToken);

        // F07-03: sólo los empleados del alcance, filtrados en la base de datos por vigencia de empleo y, con
        // sucursal, por la sucursal vigente en el rango (F06-08). Orden estable por identificador (F02-05).
        var targetEmployees = (await _reader.GetEmployeesActiveDuringAsync(request.StartDate, request.EndDate, request.BranchId, request.EmployeeId, request.EmployeeIds, cancellationToken))
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

        using var activity = AttendanceTelemetry.ActivitySource.StartActivity("attendance.recalc");
        activity?.SetTag("attendance.employees", targetEmployees.Count);
        activity?.SetTag("attendance.days", (requestEnd - requestStart).Days + 1);

        foreach (var batch in targetEmployees.Chunk(_batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                processedCount += await RunBatchAsync(batch, requestStart, requestEnd, shifts, weeklyStartDay, systemConfig, assignedAt, cancellationToken);
                successfulUnits += batch.Length;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Los lotes confirmados quedan; el lote en curso se revirtió y liberó conexión y bloqueos.
                _logger.LogWarning(
                    "Recálculo cancelado: {Successful} de {Total} empleados confirmados; el lote en curso se revirtió.",
                    successfulUnits, targetEmployees.Count);
                throw;
            }
            catch (Exception ex) when (batch.Length > 1)
            {
                // Aislamiento: el lote se revirtió entero; cada empleado se reprocesa en su propia unidad.
                _logger.LogWarning(ex, "Falló un lote de {Count} empleados; se reprocesa empleado por empleado.", batch.Length);
                foreach (var employee in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        processedCount += await RunBatchAsync(new[] { employee }, requestStart, requestEnd, shifts, weeklyStartDay, systemConfig, assignedAt, cancellationToken);
                        successfulUnits++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception unitEx)
                    {
                        _logger.LogError(unitEx, "Error en la unidad de recálculo del empleado {EmployeeId}. Se revirtió solo esa unidad.", employee.Id.Value);
                        failures.Add((new AttendanceRecalculationUnitFailure(employee.Id.Value, unitEx.GetType().Name, unitEx.Message), unitEx));
                    }
                }
            }
            catch (Exception ex)
            {
                var employee = batch[0];
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
    /// Un lote en su propia transacción con los bloqueos de sus empleados (F03-02 / F03-04). Tras confirmar se
    /// libera el seguimiento de entidades (F07-07): la memoria no crece con la plantilla.
    /// </summary>
    private async Task<int> RunBatchAsync(
        IReadOnlyList<Employee> batch,
        DateTime requestStart,
        DateTime requestEnd,
        IReadOnlyDictionary<ShiftId, Shift> shifts,
        DayOfWeek weeklyStartDay,
        SystemConfiguration? systemConfig,
        DateTime assignedAt,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "failed";
        var stats = new BatchStats();
        try
        {
            var processed = await ExclusiveAttendanceWrite.RunAsync(
                _unitOfWork,
                _lockService,
                batch.Select(e => e.Id).ToList(),
                () => RecalculateBatchAsync(batch, requestStart, requestEnd, shifts, weeklyStartDay, systemConfig, assignedAt, stats, cancellationToken),
                _ => true,
                cancellationToken);
            _unitOfWork.DiscardChanges();
            outcome = "succeeded";
            return processed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            RecordBatch(stopwatch.Elapsed.TotalMilliseconds, outcome, batch.Count, stats);
        }
    }

    private sealed class BatchStats
    {
        public int Days;
        public int Punches;
    }

    private static void RecordBatch(double elapsedMs, string outcome, int employees, BatchStats stats)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        AttendanceTelemetry.Units.Add(employees, tag);
        AttendanceTelemetry.UnitDuration.Record(elapsedMs, tag);
        if (outcome != "succeeded") return;
        AttendanceTelemetry.UnitDays.Record(stats.Days);
        AttendanceTelemetry.UnitPunches.Record(stats.Punches);
        if (stats.Days > 0)
        {
            // Coste por empleado-día del lote: cada jornada del lote comparte las lecturas, bloqueos y guardado.
            var perDay = elapsedMs / stats.Days;
            for (int i = 0; i < employees; i++) AttendanceTelemetry.EmployeeDayDuration.Record(perDay);
        }
    }

    private async Task<string> TimeZoneOfAsync(Employee employee, CancellationToken cancellationToken)
    {
        if (_branchTimeZones.TryGetValue(employee.BranchId, out var tz)) return tz;
        tz = await _timeZoneResolver.ResolveForBranchAsync(employee.BranchId, cancellationToken);
        _branchTimeZones[employee.BranchId] = tz;
        return tz;
    }

    /// <summary>
    /// Un lote atómico: carga (ya bajo bloqueo) los datos de todos sus empleados, reconstruye cada unidad y
    /// recalcula las semanas y períodos de extras. Se ejecuta dentro de la transacción del lote.
    /// </summary>
    private async Task<int> RecalculateBatchAsync(
        IReadOnlyList<Employee> batch,
        DateTime requestStart,
        DateTime requestEnd,
        IReadOnlyDictionary<ShiftId, Shift> shifts,
        DayOfWeek weeklyStartDay,
        SystemConfiguration? systemConfig,
        DateTime assignedAt,
        BatchStats stats,
        CancellationToken cancellationToken)
    {
        var worksets = new Dictionary<EmployeeId, EmployeeRecalculationWorkset>();
        foreach (var employee in batch)
        {
            var tz = await TimeZoneOfAsync(employee, cancellationToken);
            worksets[employee.Id] = new EmployeeRecalculationWorkset(employee, tz)
            {
                Lunch = systemConfig?.LunchPolicy ?? LunchPolicy.Default,
                NightLunch = systemConfig?.NightLunchPolicy ?? LunchPolicy.DefaultNight
            };
        }

        var stage = System.Diagnostics.Stopwatch.StartNew();

        // Lectura posterior al bloqueo: ningún otro escritor puede cambiar estos datos hasta el commit.
        await LoadAsync(worksets, requestStart.AddDays(-1), requestEnd.AddDays(1), cancellationToken);
        AttendanceTelemetry.RecordStage("load", stage);

        int processed = 0;
        foreach (var ws in worksets.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed += await RecalculateEmployeeAsync(ws, requestStart, requestEnd, shifts, assignedAt, cancellationToken);
            stats.Days += ws.UnitDates.Count;
            stats.Punches += ws.Records.Count;
        }
        AttendanceTelemetry.RecordStage("compute", stage);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        AttendanceTelemetry.RecordStage("save", stage);

        // F06-06 / F06-07: extras efectivas de todas las jornadas que dependen de cada unidad, leídas para todo el lote.
        var ranges = worksets.Values.ToDictionary(ws => ws.Employee.Id, ws => OvertimeRange(ws, requestStart, requestEnd, weeklyStartDay, systemConfig));
        var allAttendances = await _reader.GetDailiesAsync(
            worksets.Keys.ToList(), ranges.Values.Min(r => r.Earliest), ranges.Values.Max(r => r.Latest), cancellationToken);
        Func<DateTime, DateTime> periodStartOf = date => WorkPeriodCalculator.Containing(date, systemConfig).Start;

        bool hasSubsequentUpdates = false;
        foreach (var group in allAttendances.GroupBy(a => a.EmployeeId))
        {
            if (!worksets.TryGetValue(group.Key, out var ws)) continue;
            var range = ranges[group.Key];
            var empAttendances = group.Where(a => a.Date.Date >= range.Earliest && a.Date.Date <= range.Latest).ToList();
            var overtime = WeeklyHoursCalculationHelper.Calculate(empAttendances, ws.Employee, weeklyStartDay, periodStartOf, date => ExpectedDayOf(ws, date, shifts));
            foreach (var att in empAttendances)
            {
                if (_isClosed(att.Date)) continue; // caso 17: una jornada cerrada no cambia
                if (overtime.TryGetValue(att.Id, out var ot) &&
                    (att.OvertimeMinutes != ot.Effective || att.RawOvertimeMinutes != ot.Raw || att.WeeklyShortfallMinutes != ot.WeeklyShortfall))
                {
                    att.SetCalculatedOvertime(ot.Raw, ot.Effective, ot.WeeklyShortfall);
                    _dailyRepo.Update(att);
                    hasSubsequentUpdates = true;
                }
            }
        }

        // Las solicitudes de consolidación no se completan aquí: sólo su consumidor lo hace, comprobando la
        // versión capturada al reclamarlas, para no descartar ampliaciones concurrentes (F04-06 / A07).

        if (hasSubsequentUpdates)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        AttendanceTelemetry.RecordStage("overtime", stage);

        return processed;
    }

    /// <summary>
    /// Semanas que contienen la unidad (bolsa semanal) y, con tope de período, el período contable completo.
    /// Se recalculan siempre (no sólo con bolsa semanal) para que autorización diaria, redondeo y topes sean uniformes.
    /// </summary>
    private static (DateTime Earliest, DateTime Latest) OvertimeRange(
        EmployeeRecalculationWorkset ws, DateTime requestStart, DateTime requestEnd, DayOfWeek weeklyStartDay, SystemConfiguration? systemConfig)
    {
        var unitStart = ws.UnitDates.Count > 0 ? ws.UnitDates.Min : requestStart;
        var unitEnd = ws.UnitDates.Count > 0 ? ws.UnitDates.Max : requestEnd;
        var first = unitStart < requestStart ? unitStart : requestStart;
        var last = unitEnd > requestEnd ? unitEnd : requestEnd;
        var earliest = WeeklyHoursCalculationHelper.GetWeekStart(first, weeklyStartDay);
        var latest = WeeklyHoursCalculationHelper.GetWeekEnd(last, weeklyStartDay);
        if (ws.Employee.OvertimeCapType == OvertimeCapType.Period)
        {
            var firstPeriod = WorkPeriodCalculator.Containing(first, systemConfig);
            var lastPeriod = WorkPeriodCalculator.Containing(last, systemConfig);
            if (firstPeriod.Start < earliest) earliest = firstPeriod.Start;
            if (lastPeriod.End > latest) latest = lastPeriod.End;
        }
        return (earliest, latest);
    }

    /// <summary>
    /// Cierre F06-06: los días sin jornada calculada (futuros o fuera del empleo) se describen con la resolución
    /// vigente para la meta proporcional de la bolsa semanal en semanas partidas.
    /// </summary>
    private ExpectedDay ExpectedDayOf(EmployeeRecalculationWorkset ws, DateTime date, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var plan = ResolvePlan(ws, date, shifts);
        var dayShift = plan.Resolution.Shift;
        if (dayShift == null || plan.Resolution.IsRestDay) return new ExpectedDay(false, false, false, 0);
        var bag = dayShift.WeeklyWorkHours is { } w && w > TimeSpan.Zero;
        return new ExpectedDay(true, ws.Employee.IsActiveOn(date), bag, bag ? 0 : (int)dayShift.WorkHours.TotalMinutes);
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
        if (!_isClosed(previous) && employee.IsActiveOn(previous) &&
            (PreviousJornadaReachesFreePunch(ws, previous, shifts) || HasEarlyEntryPunch(ws, requestStart, shifts)))
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
                if (_isClosed(date))
                {
                    // Caso 17: periodo cerrado; la jornada queda como se cerró.
                }
                else if (employee.IsActiveOn(date))
                {
                    await EnsureLoadedAsync(ws, date, cancellationToken);
                    await IncludeInUnitAsync(ws, date, cancellationToken);
                    await RecalculateJornadaAsync(ws, date, shifts, assignedAt, cancellationToken);
                    processed++;
                }
                else if (employee.IsEmployedOn(date))
                {
                    // Caso 16: de vacaciones, incapacidad o baja temporal no se calcula; una jornada ya calculada
                    // (pausa registrada con fecha pasada) se elimina y sus marcajes quedan libres.
                    await EnsureLoadedAsync(ws, date, cancellationToken);
                    if (ws.ExistingDaily.ContainsKey(date.Date)) await IncludeInUnitAsync(ws, date, cancellationToken);
                }

                date = date.AddDays(1);
                continue;
            }

            // F02-03 (hacia adelante): si la unidad cambió qué marcajes del día siguiente consume,
            // la jornada siguiente ya calculada depende de este resultado y se reconstruye también.
            var next = lastDate.AddDays(1);
            if (ws.UnitDates.Count == 0 || _isClosed(next) || !employee.IsActiveOn(next)) break;

            await EnsureLoadedAsync(ws, next, cancellationToken);
            var (before, after) = ws.BoundaryClaims(DateOnly.FromDateTime(next));
            // Cierre F06-01: la jornada siguiente puede tener su entrada anticipada en el último día de la unidad.
            var nextTakesEarlyEntry = HasEarlyEntryPunch(ws, next, shifts);
            if ((before.SetEquals(after) && !nextTakesEarlyEntry) || !ws.HasPersistedJornada(next)) break;

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
        DateOnly SearchEndDate,
        DateTime? EarlyEntryStart = null);

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

            // Decisión 5: un turno mixto ya llega resuelto al horario del día (AttendanceShiftResolutionService).
            if (dayEndTime <= dayStartTime || shift.WorkHours >= TimeSpan.FromHours(24) ||
                     shift.ShiftType == ShiftType.Nocturno ||
                     shift.ShiftType == ShiftType.Flexible || shift.ShiftType == ShiftType.Partido)
            {
                isCrossDay = true;
            }
        }

        // Cierre F06-01: la entrada se acepta desde 2 h antes de la hora programada aunque caiga el día anterior
        // (jornadas que empiezan cerca de medianoche). Un flexible sin ventana de llegada no tiene hora programada.
        DateTime? earlyEntryStart = null;
        if (shift != null && !(shift.ShiftType == ShiftType.Flexible && !shift.HasEntryWindow) &&
            dayStartTime < JornadaWindowPolicy.EarlyArrivalAllowance)
        {
            earlyEntryStart = JornadaWindowPolicy.EntryWindowStart(date.Date.Add(dayStartTime));
        }

        return new JornadaPlan(
            resolution,
            adjustment,
            dayStartTime,
            dayEndTime,
            earlyEntryStart.HasValue ? workDate.AddDays(-1) : workDate,
            isCrossDay ? workDate.AddDays(1) : workDate,
            earlyEntryStart);
    }

    private static DateTime? ScheduledEnd(JornadaPlan plan, DateTime date) =>
        plan.Resolution.Shift is { } shift && !plan.Resolution.IsRestDay
            ? date.Date.Add(plan.DayStartTime).Add(shift.WorkHours)
            : null;

    /// <summary>
    /// Cierre F06-01: un marcaje entre la salida programada de una jornada y la entrada programada de la siguiente
    /// pertenece a la frontera más cercana; dentro de la duración programada siempre es de la jornada en curso.
    /// </summary>
    private static bool BelongsToNextJornada(DateTime civil, DateTime? previousScheduledEnd, DateTime nextScheduledStart)
    {
        if (previousScheduledEnd is not { } end) return true;
        if (civil <= end) return false;
        return nextScheduledStart - civil < civil - end;
    }

    /// <summary>Cierre F06-01: la jornada tiene marcajes en su ventana de entrada anticipada del día anterior.</summary>
    private bool HasEarlyEntryPunch(EmployeeRecalculationWorkset ws, DateTime date, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var plan = ResolvePlan(ws, date, shifts);
        if (plan.EarlyEntryStart is not { } start) return false;
        var previous = DateOnly.FromDateTime(date.AddDays(-1));
        return ws.Records.Values.Any(r => ws.WorkDateOf(r) == previous && r.GetCivilTime(ws.TimeZoneId) >= start);
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

        bool routesToNightly = effectiveType == ShiftType.Nocturno ||
            (plan.DayEndTime <= plan.DayStartTime &&
             effectiveType != ShiftType.Flexible && effectiveType != ShiftType.Partido);

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

        if (effectiveType == ShiftType.Flexible)
        {
            // Caso 6 (a, h): en tiempo corrido la salida es el último marcaje de la ventana y en multi-marcaje el
            // tramo abierto busca su salida en ella; ambas ventanas se anclan en el primer marcaje del día.
            var unavailable = ws.Unavailable(DateOnly.FromDateTime(date), includeOwnManual: true);
            var ownDay = PunchAssignmentPolicy.Chronological(ws.Records.Values
                .Where(r => ws.WorkDateOf(r) == plan.SearchStartDate && !unavailable.ContainsKey(r.Id)))
                .ToList();
            if (ownDay.Count == 0) return null;
            return JornadaWindowPolicy.ExitWindowEnd(
                ownDay[0].GetCivilTime(ws.TimeZoneId),
                JornadaWindowPolicy.ScheduledStart(date, shift.StartTime, shift.HasEntryWindow),
                shift.WorkHours,
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
        if (plan.Resolution.Shift is not { } nextShift || plan.Resolution.IsRestDay) return null;
        // Caso 6 (b): un flexible sin ventana de llegada no tiene hora de entrada (su inicio de 00:00 es nominal) ni
        // entrada anticipada; su jornada empieza en su propio día y no aparta los marcajes de la noche anterior.
        if (nextShift.ShiftType == ShiftType.Flexible && !nextShift.HasEntryWindow) return next.Date;
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

        // F02-04: el estado Pending/Processed deja de ser una entrada heredada; se deriva de la pertenencia.
        var unavailable = ws.Unavailable(workDate, includeOwnManual: true);
        var nextEntryStart = NextJornadaEntryStart(ws, date, shifts);
        var scheduledEnd = ScheduledEnd(plan, date);
        var previousScheduledEnd = plan.EarlyEntryStart.HasValue ? ScheduledEnd(ResolvePlan(ws, date.AddDays(-1), shifts), date.AddDays(-1)) : null;
        var windowRecords = PunchAssignmentPolicy.Chronological(ws.Records.Values
            .Where(r =>
            {
                var wd = ws.WorkDateOf(r);
                if (wd < plan.SearchStartDate || wd > plan.SearchEndDate) return false;
                var civil = r.GetCivilTime(ws.TimeZoneId);
                // Del día anterior sólo la entrada anticipada de esta jornada.
                if (wd < workDate)
                {
                    return plan.EarlyEntryStart.HasValue && civil >= plan.EarlyEntryStart.Value &&
                           BelongsToNextJornada(civil, previousScheduledEnd, date.Date.Add(plan.DayStartTime));
                }
                // La entrada anticipada de la jornada siguiente (mismo día civil) es suya.
                if (wd == workDate && nextEntryStart.HasValue && civil >= nextEntryStart.Value &&
                    BelongsToNextJornada(civil, scheduledEnd, nextEntryStart.Value + JornadaWindowPolicy.EarlyArrivalAllowance)) return false;
                return true;
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

        // La jornada se expresa en la zona del reloj donde se marcó la entrada (su primer marcaje): un empleado que marca
        // en un reloj de otra zona ve y se le calcula la hora que marcó ese reloj. Sin marcajes, la de su sucursal.
        // Límite: si la salida se marca en un reloj de otra zona, se expresa en la zona de la entrada.
        var empTimeZone = records.Select(r => r.TimeZoneId).FirstOrDefault(tz => !string.IsNullOrWhiteSpace(tz)) ?? ws.TimeZoneId;

        var draft = new PunchAssignmentDraft();

        // Delegar a subcomandos pasando Adjustment (H07), TimeZoneId y el borrador de asignaciones (F02-06)
        if (shift == null)
        {
            await _sender.Send(new ProcessNoShiftAttendanceCommand(
                employee, date, records, isRestDay, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Flexible)
        {
            await _sender.Send(new ProcessFlexibleAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, NextJornadaEntryStart: nextEntryStart, Lunch: ws.Lunch, EarlyEntryStart: plan.EarlyEntryStart), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Partido)
        {
            await _sender.Send(new ProcessSplitAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, EarlyEntryStart: plan.EarlyEntryStart, Lunch: ws.Lunch), cancellationToken);
        }
        else if (shift.ShiftType == ShiftType.Nocturno || (plan.DayEndTime <= plan.DayStartTime && shift.ShiftType != ShiftType.Flexible && shift.ShiftType != ShiftType.Partido))
        {
            await _sender.Send(new ProcessNightlyAttendanceCommand(
                employee, date, shift, records, isRestDay, plan.DayStartTime, plan.DayEndTime, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, Lunch: ws.NightLunch, NextJornadaEntryStart: nextEntryStart), cancellationToken);
        }
        else
        {
            await _sender.Send(new ProcessRegularAttendanceCommand(
                employee, date, shift, records, isRestDay, isAutoDetected, Adjustment: adjustment, TimeZoneId: empTimeZone, Draft: draft, Lunch: ws.Lunch, EarlyEntryStart: plan.EarlyEntryStart), cancellationToken);
        }

        // F02-02 / F02-06: validar el resultado completo antes de aceptarlo; un fallo revierte toda la unidad.
        if (plan.Resolution.Issue != null)
        {
            draft.DailyAttendance?.AddCalculationIssue(plan.Resolution.Issue);
        }

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
        // F07-04: una consulta para todos los marcajes que faltan (antes una por marcaje).
        var missing = recordIds.Where(id => !ws.KnownRecords.ContainsKey(id)).Distinct().ToList();
        if (missing.Count == 0) return;
        foreach (var record in await _reader.GetRecordsByIdsAsync(missing, cancellationToken))
        {
            ws.KnownRecords.TryAdd(record.Id, record);
        }
    }

    private async Task EnsureLoadedAsync(EmployeeRecalculationWorkset ws, DateTime date, CancellationToken cancellationToken)
    {
        if (date <= ws.LoadedThrough) return;

        var from = ws.LoadedThrough.AddDays(1);
        var to = date.AddDays(ExtensionLoadChunkDays - 1);
        await LoadAsync(
            new Dictionary<EmployeeId, EmployeeRecalculationWorkset> { [ws.Employee.Id] = ws },
            from, to, cancellationToken);
    }

    /// <summary>
    /// Carga los datos necesarios para calcular las jornadas [from, to]: sus rosters y ajustes, los marcajes
    /// que pueden consumir y las reclamaciones de las jornadas vecinas que compiten por ellos.
    /// </summary>
    private async Task LoadAsync(
        IReadOnlyDictionary<EmployeeId, EmployeeRecalculationWorkset> worksets,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var claimFrom = from.AddDays(-1);
        var claimTo = to.AddDays(1);

        // F07-04: una lectura por tipo de dato para todo el lote, filtrada por sus empleados.
        var window = await _reader.LoadWindowAsync(worksets.Keys.ToList(), from, to, cancellationToken);

        foreach (var daily in window.Dailies)
        {
            if (daily.Date.Date < claimFrom || daily.Date.Date > claimTo) continue;
            if (worksets.TryGetValue(daily.EmployeeId, out var ws)) ws.ExistingDaily.TryAdd(daily.Date.Date, daily);
        }

        foreach (var roster in window.Rosters)
        {
            if (roster.Date.Date < from || roster.Date.Date > to) continue;
            if (worksets.TryGetValue(roster.EmployeeId, out var ws)) ws.Rosters.TryAdd(roster.Date.Date, roster);
        }

        foreach (var adjustment in window.Adjustments)
        {
            if (adjustment.Date.Date < claimFrom || adjustment.Date.Date > claimTo) continue;
            if (worksets.TryGetValue(adjustment.EmployeeId, out var ws)) ws.Adjustments.TryAdd(adjustment.Date.Date, adjustment);
        }

        foreach (var record in window.Records)
        {
            if (worksets.TryGetValue(record.EmployeeId, out var ws)) ws.AddRecord(record);
        }

        foreach (var assignment in window.Assignments)
        {
            if (worksets.TryGetValue(assignment.EmployeeId, out var ws)) ws.TrackLoaded(assignment);
        }

        foreach (var ws in worksets.Values)
        {
            if (to > ws.LoadedThrough) ws.LoadedThrough = to;
        }

        // Marcajes referenciados manualmente: se cargan para validar propietario y fecha (H07), en una consulta.
        var manual = worksets.Values
            .SelectMany(ws => ws.Adjustments.Values.SelectMany(EmployeeRecalculationWorkset.ManualRecordIds).Select(id => (ws, id)))
            .Where(x => !x.ws.KnownRecords.ContainsKey(x.id))
            .ToList();
        if (manual.Count > 0)
        {
            var loaded = (await _reader.GetRecordsByIdsAsync(manual.Select(x => x.id).Distinct().ToList(), cancellationToken))
                .ToDictionary(r => r.Id);
            foreach (var (ws, id) in manual)
            {
                if (loaded.TryGetValue(id, out var record)) ws.KnownRecords.TryAdd(id, record);
            }
        }
    }
}
