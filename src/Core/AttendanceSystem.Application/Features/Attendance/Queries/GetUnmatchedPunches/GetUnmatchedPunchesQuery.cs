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

    public GetUnmatchedPunchesQueryHandler(IAttendanceRepository records, IEmployeeRepository employees, IDeviceRepository devices)
    {
        _records = records;
        _employees = employees;
        _devices = devices;
    }

    public async Task<IReadOnlyList<UnmatchedPunchDto>> Handle(GetUnmatchedPunchesQuery request, CancellationToken cancellationToken)
    {
        var summary = await _records.GetPunchDaySummaryAsync(request.FromDate, request.ToDate, cancellationToken);
        if (summary.Count == 0) return Array.Empty<UnmatchedPunchDto>();

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

    private static DateTime FirstHireDate(Employee employee) =>
        employee.EmploymentPeriods.Select(p => p.StartDate).DefaultIfEmpty(employee.HireDate).Min().Date;

    private static string Detail(UnmatchedPunchKind kind, Employee? employee, DateOnly firstDate) => kind switch
    {
        UnmatchedPunchKind.NotRegistered => "El ID del reloj no corresponde a ningún empleado.",
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
