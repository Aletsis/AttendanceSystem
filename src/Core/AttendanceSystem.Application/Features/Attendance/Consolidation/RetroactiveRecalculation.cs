using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Consolidation;

/// <summary>Empleados y días que se recalcularán por un cambio con vigencia pasada (caso 15).</summary>
public sealed record RecalculationImpactDto(int Employees, int Days, DateTime From, DateTime To, DateTime? ClosedThrough = null)
{
    public bool IsEmpty => Employees == 0 || Days == 0;

    /// <summary>Caso 17: los días cerrados del rango no se recalculan.</summary>
    private string ClosedNote => ClosedThrough is { } closed ? $" Los días hasta el {closed:dd/MM/yyyy} están en periodos cerrados y no se recalculan." : "";

    public string Describe() => (IsEmpty
        ? "No hay días calculados que cambien."
        : $"Se recalcularán {Employees} empleado(s), {Days} día(s) del {From:dd/MM/yyyy} al {To:dd/MM/yyyy}.") + ClosedNote;
}

/// <summary>
/// Caso 15: recálculo de lo ya calculado cuando cambia una plantilla de horario o la configuración del sistema con una
/// fecha de vigencia de hoy o pasada: se recalcula desde la vigencia hasta hoy. Las solicitudes son durables (cola de
/// consolidación): sobreviven a un reinicio. Una vigencia futura no recalcula nada: esos días aún no se calculan.
/// </summary>
public sealed class RetroactiveRecalculation
{
    private readonly IEmployeeRepository _employees;
    private readonly IDailyAttendanceRepository _dailies;
    private readonly IShiftRosterRepository _rosters;
    private readonly IShiftRepository _shifts;
    private readonly IAttendanceConsolidationRepository _consolidations;
    private readonly IConsolidationWakeup _wakeup;
    private readonly TimeProvider _timeProvider;
    private readonly AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? _closures;

    public RetroactiveRecalculation(
        IEmployeeRepository employees,
        IDailyAttendanceRepository dailies,
        IShiftRosterRepository rosters,
        IShiftRepository shifts,
        IAttendanceConsolidationRepository consolidations,
        IConsolidationWakeup wakeup,
        TimeProvider? timeProvider = null,
        AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? closures = null)
    {
        _closures = closures;
        _employees = employees;
        _dailies = dailies;
        _rosters = rosters;
        _shifts = shifts;
        _consolidations = consolidations;
        _wakeup = wakeup;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Días desde la vigencia hasta hoy (máximo el rango de recálculo); vacío si la vigencia es futura.</summary>
    private (DateTime From, DateTime To)? PastRange(DateTime effectiveFrom)
    {
        var today = _timeProvider.GetLocalNow().Date;
        var from = effectiveFrom.Date;
        if (from > today) return null;
        var earliest = today.AddDays(-DateRangePolicy.MaxRecalculationDays + 1);
        return (from < earliest ? earliest : from, today);
    }

    /// <summary>Rango a recalcular sin los días cerrados, y hasta dónde está cerrado si se recortó (caso 17).</summary>
    private async Task<((DateTime From, DateTime To)? Range, DateTime? ClosedThrough)> OpenPastRangeAsync(DateTime effectiveFrom, CancellationToken cancellationToken)
    {
        if (PastRange(effectiveFrom) is not { } range) return (null, null);
        if (_closures == null) return (range, null);
        var open = await _closures.OpenRangeAsync(range.From, range.To, cancellationToken);
        if (open is not { } r) return (null, range.To);
        return (r, r.From > range.From ? r.From.AddDays(-1) : null);
    }

    /// <summary>Empleados que usaron o usan la plantilla (directa, dentro de un mixto, en el roster o en un cambio de día).</summary>
    public async Task<IReadOnlyList<EmployeeId>> EmployeesUsingShiftAsync(ShiftId shiftId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var related = (await _shifts.GetAllAsync(cancellationToken))
            .Where(s => s.Id == shiftId || (s.ShiftType == ShiftType.Mixto && s.Days.Any(d => d.DayShiftId == shiftId)))
            .Select(s => s.Id)
            .ToHashSet();

        var ids = new HashSet<EmployeeId>();
        foreach (var daily in await _dailies.GetByDateRangeAsync(from, to, null, null, cancellationToken))
        {
            if (daily.ShiftId != null && related.Contains(daily.ShiftId)) ids.Add(daily.EmployeeId);
        }
        foreach (var roster in await _rosters.GetByDateRangeAsync(from, to, null, null, cancellationToken) ?? Array.Empty<Domain.Aggregates.ShiftRosterAggregate.ShiftRoster>())
        {
            if (roster.ShiftId != null && related.Contains(roster.ShiftId)) ids.Add(roster.EmployeeId);
        }
        foreach (var employee in await _employees.GetAllAsync(cancellationToken))
        {
            if (!employee.IsEmployedDuring(from, to)) continue;
            var uses = (employee.AssignmentOn(from).ScheduleId is { } atStart && related.Contains(atStart)) ||
                       employee.ScheduleAssignments.Any(a => a.EffectiveFrom.Date <= to && a.ScheduleId != null && related.Contains(a.ScheduleId));
            if (uses) ids.Add(employee.Id);
        }
        return ids.ToList();
    }

    public async Task<RecalculationImpactDto> ShiftImpactAsync(ShiftId shiftId, DateTime effectiveFrom, CancellationToken cancellationToken)
    {
        var (open, closedThrough) = await OpenPastRangeAsync(effectiveFrom, cancellationToken);
        if (open is not { } range) return new RecalculationImpactDto(0, 0, effectiveFrom.Date, effectiveFrom.Date, closedThrough);
        var employees = await EmployeesUsingShiftAsync(shiftId, range.From, range.To, cancellationToken);
        return new RecalculationImpactDto(employees.Count, (int)(range.To - range.From).TotalDays + 1, range.From, range.To, closedThrough);
    }

    public async Task<RecalculationImpactDto> RequestForShiftAsync(ShiftId shiftId, DateTime effectiveFrom, CancellationToken cancellationToken)
    {
        var impact = await ShiftImpactAsync(shiftId, effectiveFrom, cancellationToken);
        if (impact.IsEmpty) return impact;
        var employees = await EmployeesUsingShiftAsync(shiftId, impact.From, impact.To, cancellationToken);
        await _consolidations.RequestManyAsync(employees, impact.From, impact.To, cancellationToken);
        _wakeup.Signal();
        return impact;
    }

    private async Task<IReadOnlyList<EmployeeId>> ActiveEmployeesAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
        (await _employees.GetAllAsync(cancellationToken)).Where(e => e.IsEmployedDuring(from, to)).Select(e => e.Id).ToList();

    public async Task<RecalculationImpactDto> AllEmployeesImpactAsync(DateTime effectiveFrom, CancellationToken cancellationToken)
    {
        var (open, closedThrough) = await OpenPastRangeAsync(effectiveFrom, cancellationToken);
        if (open is not { } range) return new RecalculationImpactDto(0, 0, effectiveFrom.Date, effectiveFrom.Date, closedThrough);
        var employees = await ActiveEmployeesAsync(range.From, range.To, cancellationToken);
        return new RecalculationImpactDto(employees.Count, (int)(range.To - range.From).TotalDays + 1, range.From, range.To, closedThrough);
    }

    public async Task<RecalculationImpactDto> RequestForAllEmployeesAsync(DateTime effectiveFrom, CancellationToken cancellationToken)
    {
        var impact = await AllEmployeesImpactAsync(effectiveFrom, cancellationToken);
        if (impact.IsEmpty) return impact;
        var employees = await ActiveEmployeesAsync(impact.From, impact.To, cancellationToken);
        await _consolidations.RequestManyAsync(employees, impact.From, impact.To, cancellationToken);
        _wakeup.Signal();
        return impact;
    }
}

/// <summary>Caso 15: cuántos empleados y días recalcularía un cambio de plantilla con esa vigencia.</summary>
[RequiresPermission(Permissions.ShiftsManage)]
public sealed record GetShiftRecalculationImpactQuery(Guid ShiftId, DateTime EffectiveFrom) : IRequest<RecalculationImpactDto>;

/// <summary>Caso 15: cuántos empleados y días recalcularía un cambio de configuración con esa vigencia.</summary>
[RequiresPermission(Permissions.SettingsManage)]
public sealed record GetConfigurationRecalculationImpactQuery(DateTime EffectiveFrom) : IRequest<RecalculationImpactDto>;

public sealed class RecalculationImpactQueryHandler :
    IRequestHandler<GetShiftRecalculationImpactQuery, RecalculationImpactDto>,
    IRequestHandler<GetConfigurationRecalculationImpactQuery, RecalculationImpactDto>
{
    private readonly RetroactiveRecalculation _recalculation;

    public RecalculationImpactQueryHandler(RetroactiveRecalculation recalculation) => _recalculation = recalculation;

    public Task<RecalculationImpactDto> Handle(GetShiftRecalculationImpactQuery request, CancellationToken cancellationToken) =>
        _recalculation.ShiftImpactAsync(ShiftId.From(request.ShiftId), request.EffectiveFrom, cancellationToken);

    public Task<RecalculationImpactDto> Handle(GetConfigurationRecalculationImpactQuery request, CancellationToken cancellationToken) =>
        _recalculation.AllEmployeesImpactAsync(request.EffectiveFrom, cancellationToken);
}

/// <summary>
/// Caso 15: al iniciar el día se calcula el día anterior de todos los empleados activos ese día, para que existan las
/// faltas de quien no checó nada aunque nadie descargue ni calcule a mano (p. ej. relojes sólo por ADMS). Caso 16:
/// antes actualiza el estado de cada empleado al de hoy (empieza o termina sus vacaciones o incapacidad).
/// </summary>
[RequiresPermission(Permissions.AttendanceCalculate)]
public sealed record CalculatePreviousDayCommand : IRequest<int>;

public sealed class CalculatePreviousDayCommandHandler : IRequestHandler<CalculatePreviousDayCommand, int>
{
    private readonly IEmployeeRepository _employees;
    private readonly IAttendanceConsolidationRepository _consolidations;
    private readonly IConsolidationWakeup _wakeup;
    private readonly ILogger<CalculatePreviousDayCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IUnitOfWork? _unitOfWork;

    public CalculatePreviousDayCommandHandler(
        IEmployeeRepository employees,
        IAttendanceConsolidationRepository consolidations,
        IConsolidationWakeup wakeup,
        ILogger<CalculatePreviousDayCommandHandler> logger,
        TimeProvider? timeProvider = null,
        IUnitOfWork? unitOfWork = null)
    {
        _unitOfWork = unitOfWork;
        _employees = employees;
        _consolidations = consolidations;
        _wakeup = wakeup;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <returns>Empleados solicitados.</returns>
    public async Task<int> Handle(CalculatePreviousDayCommand request, CancellationToken cancellationToken)
    {
        var today = _timeProvider.GetLocalNow().Date;
        var yesterday = today.AddDays(-1);
        var all = await _employees.GetAllAsync(cancellationToken);

        if (_unitOfWork != null)
        {
            var refreshed = all.Where(e => e.RefreshStatus(today)).ToList();
            foreach (var employee in refreshed) _employees.Update(employee);
            if (refreshed.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Estado actualizado al de hoy para {Count} empleados (inicio o fin de vacaciones, incapacidad o baja temporal).", refreshed.Count);
            }
        }

        var active = all.Where(e => e.IsActiveOn(yesterday)).Select(e => e.Id).ToList();
        if (active.Count == 0) return 0;

        await _consolidations.RequestManyAsync(active, yesterday, yesterday, cancellationToken);
        _wakeup.Signal();
        _logger.LogInformation("Cálculo del día {Date:yyyy-MM-dd} solicitado para {Count} empleados activos.", yesterday, active.Count);
        return active.Count;
    }
}
