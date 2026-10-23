using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Shifts.Commands.CheckScheduleIntegrity;

/// <summary>
/// Caso 10 (6): revisa que ningún día pueda quedar sin turno y lo reporta como alertas de sistema, que se resuelven
/// solas cuando RH corrige la configuración. Devuelve cuántos problemas quedan.
/// </summary>
public sealed record CheckScheduleIntegrityCommand(DateTime? Today = null) : IRequest<Result<int>>;

public sealed record ScheduleAlertDto(Guid Id, DateTime TimestampUtc, string Message);

public sealed record GetScheduleAlertsQuery : IRequest<IReadOnlyList<ScheduleAlertDto>>;

public sealed class CheckScheduleIntegrityCommandHandler :
    IRequestHandler<CheckScheduleIntegrityCommand, Result<int>>,
    IRequestHandler<GetScheduleAlertsQuery, IReadOnlyList<ScheduleAlertDto>>
{
    /// <summary>Días hacia atrás que se revisan (roster con huecos y jornadas calculadas sin turno).</summary>
    public const int LookbackDays = 31;
    /// <summary>Anticipación con que se avisa que un roster está por terminar (4b).</summary>
    public const int RosterWarningDays = 15;

    private readonly IEmployeeRepository _employees;
    private readonly IShiftRepository _shifts;
    private readonly IShiftRosterRepository _rosters;
    private readonly IDailyAttendanceRepository _dailies;
    private readonly ISystemAlertRepository _alerts;
    private readonly IUnitOfWork _unitOfWork;

    public CheckScheduleIntegrityCommandHandler(
        IEmployeeRepository employees,
        IShiftRepository shifts,
        IShiftRosterRepository rosters,
        IDailyAttendanceRepository dailies,
        ISystemAlertRepository alerts,
        IUnitOfWork unitOfWork)
    {
        _employees = employees;
        _shifts = shifts;
        _rosters = rosters;
        _dailies = dailies;
        _alerts = alerts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(CheckScheduleIntegrityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var today = (request.Today ?? DateTime.Today).Date;
            var problems = await FindProblemsAsync(today, cancellationToken);

            var active = (await _alerts.GetActiveAlertsAsync(cancellationToken)).Where(a => a.Type == AlertType.ScheduleIntegrity).ToList();
            foreach (var alert in active)
            {
                if (alert.ReferenceId != null && problems.Remove(alert.ReferenceId, out var message))
                {
                    if (alert.Message != message) alert.UpdateMessage(message);
                }
                else
                {
                    alert.Resolve();
                }
            }
            foreach (var (key, message) in problems)
            {
                await _alerts.AddAsync(SystemAlert.Create(AlertType.ScheduleIntegrity, key, message), cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<int>.Success(active.Count(a => !a.IsResolved) + problems.Count);
        }
        catch (Exception ex)
        {
            return Result<int>.Failure($"Error al revisar los horarios: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ScheduleAlertDto>> Handle(GetScheduleAlertsQuery request, CancellationToken cancellationToken) =>
        (await _alerts.GetActiveAlertsAsync(cancellationToken))
            .Where(a => a.Type == AlertType.ScheduleIntegrity)
            .OrderBy(a => a.Message)
            .Select(a => new ScheduleAlertDto(a.Id.Value, a.Timestamp, a.Message))
            .ToList();

    /// <summary>Problemas vigentes por clave estable (la referencia de la alerta).</summary>
    private async Task<Dictionary<string, string>> FindProblemsAsync(DateTime today, CancellationToken cancellationToken)
    {
        var problems = new Dictionary<string, string>();
        var shifts = (await _shifts.GetAllAsync(cancellationToken)).ToDictionary(s => s.Id);
        var employees = await _employees.GetAllAsync(cancellationToken);
        var names = employees.ToDictionary(e => e.Id, e => $"{e.FirstName} {e.LastName} ({e.Id.Value})".Trim());

        foreach (var employee in employees.Where(e => e.IsActiveOn(today)))
        {
            if (ScheduleProblem(employee, today, shifts) is { } scheduleProblem)
                problems[$"horario:{employee.Id.Value}"] = $"{names[employee.Id]} {scheduleProblem}";

            if (await RosterProblemAsync(employee, today, shifts, cancellationToken) is { } rosterProblem)
                problems[$"roster:{employee.Id.Value}"] = $"{names[employee.Id]}: {rosterProblem}";
        }

        foreach (var mixed in shifts.Values.Where(s => s.ShiftType == ShiftType.Mixto))
        {
            if (MixedProblem(mixed, shifts) is { } mixedProblem)
                problems[$"mixto:{mixed.Id.Value}"] = mixedProblem;
        }

        // Respaldo: jornadas que de todos modos se calcularon sin turno.
        var withoutShift = (await _dailies.GetByDateRangeAsync(today.AddDays(-LookbackDays), today, cancellationToken: cancellationToken))
            .Where(d => d.IsWithoutShift)
            .GroupBy(d => d.EmployeeId);
        foreach (var days in withoutShift)
        {
            var dates = days.Select(d => d.Date.Date).OrderBy(d => d).ToList();
            var name = names.GetValueOrDefault(days.Key, days.Key.Value);
            problems[$"sin-turno:{days.Key.Value}"] =
                $"{name}: {dates.Count} jornada(s) calculadas sin turno entre el {dates[0]:dd/MM/yyyy} y el {dates[^1]:dd/MM/yyyy}. " +
                "Corrija su horario y recalcule esas fechas.";
        }

        return problems;
    }

    /// <summary>Turno y horario de la vigencia actual y de las futuras.</summary>
    private static string? ScheduleProblem(Employee employee, DateTime today, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var dates = employee.ScheduleAssignments.Select(a => a.EffectiveFrom.Date).Where(d => d > today).Prepend(today).Distinct().OrderBy(d => d);
        foreach (var date in dates)
        {
            var (scheduleId, shiftType, _) = employee.AssignmentOn(date);
            var since = date > today ? $" a partir del {date:dd/MM/yyyy}" : "";
            if (!shiftType.HasValue) return $"no tiene turno{since}: asígnele uno.";
            if (shiftType.Value == ShiftType.Rotativo) continue;
            if (scheduleId == null) return $"no tiene horario{since}: asígnele uno.";
            if (!shifts.ContainsKey(scheduleId)) return $"tiene un horario que ya no existe{since}: asígnele otro.";
        }
        return null;
    }

    /// <summary>Cada día como rotativo (del último mes a los próximos 15 días) debe tener turno o descanso en el roster.</summary>
    private async Task<string?> RosterProblemAsync(Employee employee, DateTime today, IReadOnlyDictionary<ShiftId, Shift> shifts, CancellationToken cancellationToken)
    {
        var start = today.AddDays(-LookbackDays);
        var end = today.AddDays(RosterWarningDays);
        var rotatingDates = Enumerable.Range(0, (end - start).Days + 1).Select(i => start.AddDays(i))
            .Where(d => employee.IsActiveOn(d) && employee.AssignmentOn(d).ShiftType == ShiftType.Rotativo)
            .ToList();
        if (rotatingDates.Count == 0) return null;

        var covered = (await _rosters.GetByEmployeeAsync(employee.Id, start, end, cancellationToken))
            .Where(r => r.IsRestDay || (r.ShiftId != null && shifts.ContainsKey(r.ShiftId)))
            .Select(r => r.Date.Date)
            .ToHashSet();
        var missing = rotatingDates.Where(d => !covered.Contains(d)).ToList();
        if (missing.Count == 0) return null;

        // Termina: desde el primer faltante (hoy o después) ya no hay roster.
        var first = missing[0];
        if (first >= today && missing.Count == rotatingDates.Count(d => d >= first))
            return $"su roster rotativo termina el {first.AddDays(-1):dd/MM/yyyy}; genere o extienda su esquema.";

        var shown = string.Join(", ", missing.Take(5).Select(d => d.ToString("dd/MM/yyyy")));
        return $"su roster rotativo no tiene turno ni descanso para {missing.Count} día(s): {shown}{(missing.Count > 5 ? "…" : "")}.";
    }

    private static string? MixedProblem(Shift mixed, IReadOnlyDictionary<ShiftId, Shift> shifts)
    {
        var missing = MixedShiftSchedule.MissingDays(mixed.Days.Where(d => d.DayShiftId != null).Select(d => d.DayOfWeek));
        if (missing.Count > 0)
            return $"El turno mixto '{mixed.Name}' no tiene horario para: {string.Join(", ", missing.Select(MixedShiftSchedule.DayName))}. Edítelo y asigne uno.";

        var invalid = mixed.Days
            .Where(d => !shifts.TryGetValue(d.DayShiftId!, out var dayShift) || dayShift.ShiftType is ShiftType.Mixto or ShiftType.Rotativo)
            .Select(d => MixedShiftSchedule.DayName(d.DayOfWeek))
            .ToList();
        return invalid.Count > 0
            ? $"El turno mixto '{mixed.Name}' tiene un horario inexistente o inválido para: {string.Join(", ", invalid)}. Edítelo y asigne uno."
            : null;
    }
}
