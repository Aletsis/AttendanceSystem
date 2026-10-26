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
/// días, en orden. Un periodo reabierto no se vuelve a cerrar solo: lo cierra RH cuando termine de corregir.
/// </summary>
[RequiresPermission(Permissions.AttendanceClosePeriod)]
public sealed record AutoClosePeriodsCommand : IRequest<int>;

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

    public PeriodClosureHandlers(
        IAttendancePeriodClosureRepository closures,
        ISystemConfigurationRepository configuration,
        IDailyAttendanceRepository dailies,
        IUnitOfWork unitOfWork,
        ISender sender,
        ILogger<PeriodClosureHandlers> logger,
        ICurrentUserService? currentUser = null,
        TimeProvider? timeProvider = null)
    {
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

    /// <summary>Por qué no se puede cerrar el periodo; null si se puede.</summary>
    private string? CannotClose(IReadOnlyList<AttendancePeriodClosure> all, SystemConfiguration config, AttendancePeriod period)
    {
        if (AttendancePeriods.Containing(config, period.Start) != period) return "Las fechas no corresponden a un periodo de la configuración.";
        if (period.End >= Today) return "El periodo aún no termina.";
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
                CanClose: state == PeriodState.Open && CannotClose(all, config, period) == null,
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
        if (CannotClose(all, config, period) is { } error) return Result.Failure(error);

        await _closures.AddAsync(AttendancePeriodClosure.Close(period.Start, period.End, Author, automatic: false), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
        var config = await ConfigAsync(cancellationToken);
        var all = (await _closures.GetAllAsync(cancellationToken)).ToList();
        bool Due(AttendancePeriod p) => Today > p.End.AddDays(PeriodClosureService.AutomaticCloseAfterDays);
        bool EverClosed(AttendancePeriod p) => all.Any(c => c.StartDate == p.Start && c.EndDate == p.End);

        AttendancePeriod candidate;
        if (LastActive(all) is { } last)
        {
            candidate = AttendancePeriods.Next(config, new AttendancePeriod(last.StartDate, last.EndDate));
        }
        else
        {
            // Sin cierres: el periodo más reciente ya vencido es el primero.
            candidate = AttendancePeriods.Containing(config, Today);
            while (!Due(candidate)) candidate = AttendancePeriods.Previous(config, candidate);
        }

        var closed = 0;
        while (Due(candidate) && !EverClosed(candidate) && CannotClose(all, config, candidate) == null)
        {
            var closure = AttendancePeriodClosure.Close(candidate.Start, candidate.End, "Cierre automático", automatic: true);
            await _closures.AddAsync(closure, cancellationToken);
            all.Add(closure);
            closed++;
            _logger.LogInformation("Periodo {Period} cerrado automáticamente.", candidate.Describe());
            candidate = AttendancePeriods.Next(config, candidate);
        }
        if (closed > 0) await _unitOfWork.SaveChangesAsync(cancellationToken);
        return closed;
    }
}
