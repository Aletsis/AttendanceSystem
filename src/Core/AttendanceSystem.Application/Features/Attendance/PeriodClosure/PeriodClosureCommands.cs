using AttendanceSystem.Application.Features.Attendance.Queries.GetUnmatchedPunches;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;

namespace AttendanceSystem.Application.Features.Attendance.PeriodClosure;

public enum PeriodState
{
    /// <summary>Aún no termina.</summary>
    InProgress = 0,
    Open = 1,
    Closed = 2,
}

public sealed record PeriodClosureRowDto(
    DateTime Start,
    DateTime End,
    PeriodState State,
    Guid? ClosureId,
    string? ClosedBy,
    DateTime? ClosedAt,
    bool Automatic,
    string? LastReopenedBy,
    DateTime? LastReopenedAt,
    string? LastReopenReason,
    bool CanClose,
    bool CanReopen,
    DateTime? AutomaticCloseOn);

/// <summary>Pendientes de un periodo, para revisarlos antes de cerrarlo (no impiden el cierre).</summary>
public sealed record PeriodPendingSummaryDto(int DaysWithIssues, int DaysWithPendingExits, int DaysWithUnauthorizedOvertime, int UnmatchedPunchIds)
{
    public bool IsEmpty => DaysWithIssues + DaysWithPendingExits + DaysWithUnauthorizedOvertime + UnmatchedPunchIds == 0;
}

/// <summary>Caso 17: periodos recientes con su estado de cierre, del más reciente al más antiguo.</summary>
[RequiresPermission(Permissions.AttendanceClosePeriod)]
public sealed record GetPeriodClosuresQuery(int Count = 12) : IRequest<IReadOnlyList<PeriodClosureRowDto>>;

[RequiresPermission(Permissions.AttendanceClosePeriod)]
public sealed record GetPeriodPendingSummaryQuery(DateTime Start, DateTime End) : IRequest<PeriodPendingSummaryDto>;

/// <summary>Cierres vigentes que tocan el rango (para avisar en reportes que el periodo está cerrado).</summary>
[RequiresPermission(Permissions.ReportsView)]
public sealed record GetClosedPeriodsInRangeQuery(DateTime From, DateTime To) : IRequest<IReadOnlyList<AttendancePeriod>>;

/// <summary>
/// Caso 17: cierra un periodo terminado. Sólo el que sigue al último cerrado (o cualquiera si no hay cerrados), para
/// que no queden huecos. Los pendientes se muestran antes, pero no lo impiden.
/// </summary>
[RequiresPermission(Permissions.AttendanceClosePeriod)]
public sealed record ClosePeriodCommand(DateTime Start, DateTime End) : IRequest<Result>;

/// <summary>Caso 17: reabre el último periodo cerrado (sólo Administrador, con motivo).</summary>
[RequiresPermission(Permissions.AttendanceReopenPeriod)]
public sealed record ReopenPeriodCommand(Guid ClosureId, string Reason) : IRequest<Result>;

/// <summary>
/// Caso 17: cierra solos los periodos que terminaron hace más de <see cref="PeriodClosureService.AutomaticCloseAfterDays"/>
/// días, en orden. Un periodo reabierto no se vuelve a cerrar solo: lo cierra RH cuando termine de corregir. Si quedan
/// días del periodo sin calcular (solicitudes de cálculo pendientes o fallidas), el cierre se pospone con una alerta.
/// </summary>
/// <param name="Today">Fecha de negocio de la ejecución (la fija la tarea programada); sin ella, la fecha local.</param>
[RequiresPermission(Permissions.AttendanceClosePeriod)]
public sealed record AutoClosePeriodsCommand(DateTime? Today = null) : IRequest<int>;

public sealed class PeriodClosureHandlers :
    IRequestHandler<GetPeriodClosuresQuery, IReadOnlyList<PeriodClosureRowDto>>,
    IRequestHandler<GetPeriodPendingSummaryQuery, PeriodPendingSummaryDto>,
    IRequestHandler<GetClosedPeriodsInRangeQuery, IReadOnlyList<AttendancePeriod>>,
    IRequestHandler<ClosePeriodCommand, Result>,
    IRequestHandler<ReopenPeriodCommand, Result>,
    IRequestHandler<AutoClosePeriodsCommand, int>
{
    private readonly IAttendancePeriodClosureRepository _closures;
    private readonly ISystemConfigurationRepository _configuration;
    private readonly IDailyAttendanceRepository _dailies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISender _sender;
    private readonly ICurrentUserService? _currentUser;
    private readonly ILogger<PeriodClosureHandlers> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IAttendanceConsolidationRepository? _consolidations;
    private readonly AttendanceSystem.Application.Features.Automation.OperationalAlerts? _alerts;

    public PeriodClosureHandlers(
        IAttendancePeriodClosureRepository closures,
        ISystemConfigurationRepository configuration,
        IDailyAttendanceRepository dailies,
        IUnitOfWork unitOfWork,
        ISender sender,
        ILogger<PeriodClosureHandlers> logger,
        ICurrentUserService? currentUser = null,
        TimeProvider? timeProvider = null,
        IAttendanceConsolidationRepository? consolidations = null,
        AttendanceSystem.Application.Features.Automation.OperationalAlerts? alerts = null)
    {
        _consolidations = consolidations;
        _alerts = alerts;
        _closures = closures;
        _configuration = configuration;
        _dailies = dailies;
        _unitOfWork = unitOfWork;
        _sender = sender;
        _logger = logger;
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime Today => _timeProvider.GetLocalNow().Date;
    private string Author => _currentUser is { IsAuthenticated: true, UserName: { Length: > 0 } name } ? name : "Sistema";

    private async Task<SystemConfiguration> ConfigAsync(CancellationToken cancellationToken) =>
        await _configuration.GetConfigurationAsync(cancellationToken) ?? SystemConfiguration.CreateDefault();

    private static AttendancePeriodClosure? ActiveOf(IEnumerable<AttendancePeriodClosure> all, AttendancePeriod period) =>
        all.FirstOrDefault(c => c.IsActive && c.StartDate == period.Start && c.EndDate == period.End);

    private static AttendancePeriodClosure? LastActive(IEnumerable<AttendancePeriodClosure> all) =>
        all.Where(c => c.IsActive).OrderByDescending(c => c.EndDate).FirstOrDefault();

    /// <summary>Clave de la alerta de un cierre automático pospuesto.</summary>
    public static string PostponedAlertKey(AttendancePeriod period) => $"cierre:{period.Start:yyyyMMdd}";

    /// <summary>Por qué no se puede cerrar el periodo; null si se puede.</summary>
    private string? CannotClose(IReadOnlyList<AttendancePeriodClosure> all, SystemConfiguration config, AttendancePeriod period, DateTime today)
    {
        if (AttendancePeriods.Containing(config, period.Start) != period) return "Las fechas no corresponden a un periodo de la configuración.";
        if (period.End >= today) return "El periodo aún no termina.";
        if (ActiveOf(all, period) != null) return "El periodo ya está cerrado.";
        if (LastActive(all) is { } last)
        {
            if (period.End <= last.EndDate) return "No se puede cerrar un periodo anterior al último cerrado.";
            if (AttendancePeriods.Previous(config, period).End != last.EndDate) return "Cierre primero el periodo anterior.";
        }
        return null;
    }

    public async Task<IReadOnlyList<PeriodClosureRowDto>> Handle(GetPeriodClosuresQuery request, CancellationToken cancellationToken)
    {
        var config = await ConfigAsync(cancellationToken);
        var all = await _closures.GetAllAsync(cancellationToken);
        var lastActive = LastActive(all);
        var rows = new List<PeriodClosureRowDto>();
        var period = AttendancePeriods.Containing(config, Today);
        for (int i = 0; i < Math.Clamp(request.Count, 1, 60); i++, period = AttendancePeriods.Previous(config, period))
        {
            var active = ActiveOf(all, period);
            var reopened = all.Where(c => !c.IsActive && c.StartDate == period.Start && c.EndDate == period.End).OrderByDescending(c => c.ReopenedAt).FirstOrDefault();
            var state = period.End >= Today ? PeriodState.InProgress : active != null ? PeriodState.Closed : PeriodState.Open;
            rows.Add(new PeriodClosureRowDto(
                period.Start, period.End, state, active?.Id, active?.ClosedBy, active?.ClosedAt, active?.Automatic ?? false,
                reopened?.ReopenedBy, reopened?.ReopenedAt, reopened?.ReopenReason,
                CanClose: state == PeriodState.Open && CannotClose(all, config, period, Today) == null,
                CanReopen: active != null && active.Id == lastActive?.Id,
                AutomaticCloseOn: state != PeriodState.Closed && reopened == null ? period.End.AddDays(PeriodClosureService.AutomaticCloseAfterDays + 1) : null));
        }
        return rows;
    }

    public async Task<PeriodPendingSummaryDto> Handle(GetPeriodPendingSummaryQuery request, CancellationToken cancellationToken)
    {
        var days = await _dailies.GetByDateRangeAsync(request.Start.Date, request.End.Date, null, null, cancellationToken);
        var unmatched = await _sender.Send(new GetUnmatchedPunchesQuery(DateOnly.FromDateTime(request.Start), DateOnly.FromDateTime(request.End)), cancellationToken);
        return new PeriodPendingSummaryDto(
            days.Count(d => !string.IsNullOrWhiteSpace(d.CalculationIssue)),
            days.Count(d => d.HasTemporaryExits && d.TemporaryExits.Any(e => e.Status == TemporaryExitStatus.Pending)),
            days.Count(d => d.HasUnauthorizedOvertime),
            unmatched.Select(u => u.EmployeeId).Distinct().Count());
    }

    public async Task<IReadOnlyList<AttendancePeriod>> Handle(GetClosedPeriodsInRangeQuery request, CancellationToken cancellationToken) =>
        (await _closures.GetAllAsync(cancellationToken))
            .Where(c => c.IsActive && c.StartDate <= request.To.Date && c.EndDate >= request.From.Date)
            .OrderBy(c => c.StartDate)
            .Select(c => new AttendancePeriod(c.StartDate, c.EndDate))
            .ToList();

    public async Task<Result> Handle(ClosePeriodCommand request, CancellationToken cancellationToken)
    {
        var config = await ConfigAsync(cancellationToken);
        var all = await _closures.GetAllAsync(cancellationToken);
        var period = new AttendancePeriod(request.Start.Date, request.End.Date);
        if (CannotClose(all, config, period, Today) is { } error) return Result.Failure(error);

        await _closures.AddAsync(AttendancePeriodClosure.Close(period.Start, period.End, Author, automatic: false), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (_alerts != null) await _alerts.ResolveAsync(PostponedAlertKey(period), cancellationToken);
        _logger.LogWarning("Periodo {Period} cerrado por {Author}.", period.Describe(), Author);
        return Result.Success();
    }

    public async Task<Result> Handle(ReopenPeriodCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result.Failure("Indique el motivo para reabrir el periodo.");
        var all = await _closures.GetAllAsync(cancellationToken);
        var closure = all.FirstOrDefault(c => c.Id == request.ClosureId && c.IsActive);
        if (closure == null) return Result.Failure("El periodo no está cerrado.");
        if (LastActive(all)?.Id != closure.Id) return Result.Failure("Sólo se puede reabrir el último periodo cerrado.");

        closure.Reopen(Author, request.Reason);
        _closures.Update(closure);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Periodo del {Start:dd/MM/yyyy} al {End:dd/MM/yyyy} reabierto por {Author}: {Reason}", closure.StartDate, closure.EndDate, Author, request.Reason);
        return Result.Success();
    }

    public async Task<int> Handle(AutoClosePeriodsCommand request, CancellationToken cancellationToken)
    {
        var today = (request.Today ?? Today).Date;
        var config = await ConfigAsync(cancellationToken);
        var all = (await _closures.GetAllAsync(cancellationToken)).ToList();
        bool Due(AttendancePeriod p) => today > p.End.AddDays(PeriodClosureService.AutomaticCloseAfterDays);
        bool EverClosed(AttendancePeriod p) => all.Any(c => c.StartDate == p.Start && c.EndDate == p.End);

        AttendancePeriod candidate;
        if (LastActive(all) is { } last)
        {
            candidate = AttendancePeriods.Next(config, new AttendancePeriod(last.StartDate, last.EndDate));
        }
        else
        {
            // Sin cierres: el periodo más reciente ya vencido es el primero.
            candidate = AttendancePeriods.Containing(config, today);
            while (!Due(candidate)) candidate = AttendancePeriods.Previous(config, candidate);
        }

        var closedPeriods = new List<AttendancePeriod>();
        while (Due(candidate) && !EverClosed(candidate) && CannotClose(all, config, candidate, today) == null)
        {
            // Días sin calcular: cerrar congelaría resultados incompletos. Se pospone (y los siguientes, que van en orden).
            var unfinished = _consolidations == null ? 0 : await _consolidations.CountUnfinishedInRangeAsync(candidate.Start, candidate.End, cancellationToken);
            if (unfinished > 0)
            {
                _logger.LogWarning("Cierre automático del periodo {Period} pospuesto: {Count} solicitudes de cálculo pendientes o fallidas.", candidate.Describe(), unfinished);
                if (_alerts != null)
                {
                    await _alerts.RaiseAsync(PostponedAlertKey(candidate), "Cierre automático de periodo pospuesto",
                        $"El cierre automático del periodo {candidate.Describe()} se pospuso: {unfinished} solicitudes de cálculo de asistencia " +
                        "siguen pendientes o fallidas en esas fechas. Se cerrará solo en cuanto terminen de calcularse; si alguna falló, " +
                        "corrija la causa y reinténtela.", cancellationToken);
                }
                break;
            }

            var closure = AttendancePeriodClosure.Close(candidate.Start, candidate.End, "Cierre automático", automatic: true);
            await _closures.AddAsync(closure, cancellationToken);
            all.Add(closure);
            closedPeriods.Add(candidate);
            _logger.LogInformation("Periodo {Period} cerrado automáticamente.", candidate.Describe());
            candidate = AttendancePeriods.Next(config, candidate);
        }
        if (closedPeriods.Count > 0) await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (_alerts != null)
        {
            foreach (var period in closedPeriods) await _alerts.ResolveAsync(PostponedAlertKey(period), cancellationToken);
        }
        return closedPeriods.Count;
    }
}
