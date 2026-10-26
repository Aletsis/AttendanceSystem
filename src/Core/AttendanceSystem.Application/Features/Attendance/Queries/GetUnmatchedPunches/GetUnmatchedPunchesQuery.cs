using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetUnmatchedPunches;

/// <summary>Por qué los marcajes de un ID no cuentan en el cálculo (caso 14).</summary>
public enum UnmatchedPunchKind
{
    /// <summary>El ID del reloj no corresponde a ningún empleado: hay que darlo de alta.</summary>
    NotRegistered = 1,
    /// <summary>Checó antes de su fecha de ingreso (suele ser una fecha de ingreso mal capturada).</summary>
    BeforeHire = 2,
    /// <summary>Siguió checando después de su baja.</summary>
    AfterTermination = 3,
    /// <summary>Caso 16: checó durante sus vacaciones, incapacidad o baja temporal (regresó antes o el período está mal).</summary>
    DuringLeave = 4,
    /// <summary>Caso 17: llegó después de cerrar su periodo; no se calculó (el periodo se reabre si debe contar).</summary>
    ArrivedAfterClosure = 5,
}

public sealed record UnmatchedPunchDto(
    string EmployeeId,
    string? EmployeeName,
    UnmatchedPunchKind Kind,
    string Detail,
    string Devices,
    int Count,
    DateOnly FirstDate,
    DateOnly LastDate);

/// <summary>
/// Caso 14: marcajes vigentes del rango que el cálculo no usa porque su ID no es de un empleado registrado o porque el
/// empleado no estaba en un periodo de empleo ese día. Se calcula al consultar: al dar de alta, reingresar o corregir
/// la fecha de ingreso, el aviso desaparece solo. Un renglón por ID y motivo.
/// </summary>
[RequiresPermission(Permissions.EmployeesView)]
public sealed record GetUnmatchedPunchesQuery(DateOnly FromDate, DateOnly ToDate) : IRequest<IReadOnlyList<UnmatchedPunchDto>>;

public sealed class GetUnmatchedPunchesQueryHandler : IRequestHandler<GetUnmatchedPunchesQuery, IReadOnlyList<UnmatchedPunchDto>>
{
    /// <summary>Rango por omisión del aviso en el menú y el inicio.</summary>
    public const int DefaultLookbackDays = 60;

    private readonly IAttendanceRepository _records;
    private readonly IEmployeeRepository _employees;
    private readonly IDeviceRepository _devices;
    private readonly AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? _closures;

    public GetUnmatchedPunchesQueryHandler(
        IAttendanceRepository records,
        IEmployeeRepository employees,
        IDeviceRepository devices,
        AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService? closures = null)
    {
        _records = records;
        _employees = employees;
        _devices = devices;
        _closures = closures;
    }

    public async Task<IReadOnlyList<UnmatchedPunchDto>> Handle(GetUnmatchedPunchesQuery request, CancellationToken cancellationToken)
    {
        var summary = (await _records.GetPunchDaySummaryAsync(request.FromDate, request.ToDate, cancellationToken)).ToList();
        var late = await ArrivedAfterClosureAsync(request, cancellationToken);
        if (summary.Count == 0 && late.Count == 0) return Array.Empty<UnmatchedPunchDto>();

        var employees = (await _employees.GetAllAsync(cancellationToken)).ToDictionary(e => e.Id);
        var deviceNames = (await _devices.GetAllDevicesAsync(cancellationToken)).ToDictionary(d => d.Id, d => d.Name);

        var unmatched = new List<(EmployeeId Id, UnmatchedPunchKind Kind, PunchDaySummary Day)>();
        foreach (var day in summary)
        {
            if (!employees.TryGetValue(day.EmployeeId, out var employee))
            {
                unmatched.Add((day.EmployeeId, UnmatchedPunchKind.NotRegistered, day));
                continue;
            }
            var date = day.WorkDate.ToDateTime(TimeOnly.MinValue);
            if (employee.IsActiveOn(date)) continue;
            var kind = employee.IsEmployedOn(date) ? UnmatchedPunchKind.DuringLeave
                : date < FirstHireDate(employee) ? UnmatchedPunchKind.BeforeHire
                : UnmatchedPunchKind.AfterTermination;
            unmatched.Add((day.EmployeeId, kind, day));
        }
        // Los de IDs no registrados o fuera de su periodo de empleo ya aparecen por ese motivo.
        unmatched.AddRange(late
            .Where(day => employees.TryGetValue(day.EmployeeId, out var e) && e.IsActiveOn(day.WorkDate.ToDateTime(TimeOnly.MinValue)))
            .Select(day => (day.EmployeeId, UnmatchedPunchKind.ArrivedAfterClosure, day)));

        return unmatched
            .GroupBy(u => (u.Id, u.Kind))
            .Select(g =>
            {
                employees.TryGetValue(g.Key.Id, out var employee);
                var first = g.Min(u => u.Day.WorkDate);
                var last = g.Max(u => u.Day.WorkDate);
                var devices = string.Join(", ", g.Select(u => deviceNames.GetValueOrDefault(u.Day.DeviceId, u.Day.DeviceId.Value)).Distinct().OrderBy(n => n));
                return new UnmatchedPunchDto(
                    g.Key.Id.Value,
                    employee?.GetFullName(),
                    g.Key.Kind,
                    Detail(g.Key.Kind, employee, first),
                    devices,
                    g.Sum(u => u.Day.Count),
                    first,
                    last);
            })
            .OrderBy(u => u.Kind)
            .ThenByDescending(u => u.LastDate)
            .ToList();
    }

    /// <summary>Marcajes de días cerrados que llegaron después del cierre de su periodo (por empleado, día y reloj).</summary>
    private async Task<IReadOnlyList<PunchDaySummary>> ArrivedAfterClosureAsync(GetUnmatchedPunchesQuery request, CancellationToken cancellationToken)
    {
        if (_closures == null) return Array.Empty<PunchDaySummary>();
        var closures = (await _closures.ActiveAsync(cancellationToken))
            .Where(c => c.StartDate <= request.ToDate.ToDateTime(TimeOnly.MinValue) && c.EndDate >= request.FromDate.ToDateTime(TimeOnly.MinValue))
            .ToList();
        if (closures.Count == 0) return Array.Empty<PunchDaySummary>();

        var records = await _records.GetReceivedAfterAsync(closures.Min(c => c.ClosedAt),
            DateOnly.FromDateTime(closures.Min(c => c.StartDate)), DateOnly.FromDateTime(closures.Max(c => c.EndDate)), cancellationToken);
        return records
            .Where(r => closures.Any(c => c.Contains(r.GetWorkDate().ToDateTime(TimeOnly.MinValue)) && r.ReceivedAt > c.ClosedAt))
            .GroupBy(r => (r.EmployeeId, r.GetWorkDate(), r.DeviceId))
            .Select(g => new PunchDaySummary(g.Key.EmployeeId, g.Key.Item2, g.Key.DeviceId, g.Count()))
            .ToList();
    }

    private static DateTime FirstHireDate(Employee employee) =>
        employee.EmploymentPeriods.Select(p => p.StartDate).DefaultIfEmpty(employee.HireDate).Min().Date;

    private static string Detail(UnmatchedPunchKind kind, Employee? employee, DateOnly firstDate) => kind switch
    {
        UnmatchedPunchKind.NotRegistered => "El ID del reloj no corresponde a ningún empleado.",
        UnmatchedPunchKind.ArrivedAfterClosure => "Llegó después de cerrar su periodo y no se calculó; reabra el periodo si debe contar.",
        UnmatchedPunchKind.BeforeHire => $"Checó antes de su ingreso del {FirstHireDate(employee!):dd/MM/yyyy}.",
        UnmatchedPunchKind.DuringLeave => employee!.LeaveOn(firstDate.ToDateTime(TimeOnly.MinValue)) is { } leave
            ? $"Checó durante su {LeaveName(leave.Status)} del {leave.StartDate:dd/MM/yyyy}{(leave.EndDate is { } end ? $" al {end:dd/MM/yyyy}" : " (sin fecha de regreso)")}."
            : "Checó durante vacaciones, incapacidad o baja temporal.",
        _ => TerminationBefore(employee!, firstDate) is { } end
            ? $"Siguió checando después de su baja del {end:dd/MM/yyyy}."
            : "Siguió checando sin estar dado de alta."
    };

    public static string LeaveName(EmployeeStatus status) => status switch
    {
        EmployeeStatus.Vacaciones => "periodo de vacaciones",
        EmployeeStatus.Incapacidad => "incapacidad",
        _ => "baja temporal"
    };

    private static DateTime? TerminationBefore(Employee employee, DateOnly date)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        return employee.EmploymentPeriods
            .Where(p => p.EndDate.HasValue && p.EndDate.Value < day)
            .Select(p => p.EndDate)
            .DefaultIfEmpty(employee.TerminationDate is { } t && t < day ? t : null)
            .Max();
    }
}
