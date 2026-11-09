using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetLunchBoard;

/// <summary>Un empleado que está ahora en su comida (salió y aún no regresa).</summary>
/// <param name="LunchStartCivil">Hora civil (de su sucursal) en que salió a comer.</param>
/// <param name="NowCivil">Hora civil de su sucursal al consultar: el tablero suma desde aquí el tiempo transcurrido.</param>
/// <param name="AllowedMinutes">Minutos de comida con goce que le aplican (del turno o globales).</param>
public sealed record LunchBoardEntryDto(
    string EmployeeId,
    string EmployeeName,
    string? BranchName,
    string? DepartmentName,
    string? ShiftName,
    string? Photo,
    DateTime LunchStartCivil,
    DateTime NowCivil,
    int AllowedMinutes,
    string? DeviceName);

/// <summary>
/// Tablero de comidas: empleados activos cuya jornada en curso tiene una salida intermedia sin regreso que es su comida
/// según <see cref="LunchBoardDetector"/>. Se calcula de los marcajes vigentes al consultar (no del cálculo diario), así
/// que la tarjeta aparece en cuanto llega la salida y desaparece en cuanto llega el regreso.
/// </summary>
[RequiresPermission(Permissions.AttendanceView)]
public sealed record GetLunchBoardQuery : IRequest<IReadOnlyList<LunchBoardEntryDto>>;

public sealed class GetLunchBoardQueryHandler : IRequestHandler<GetLunchBoardQuery, IReadOnlyList<LunchBoardEntryDto>>
{
    private readonly IAttendanceRepository _records;
    private readonly IEmployeeRepository _employees;
    private readonly IBranchRepository _branches;
    private readonly IDepartmentRepository _departments;
    private readonly IShiftRepository _shifts;
    private readonly IShiftRosterRepository _rosters;
    private readonly IDeviceRepository _devices;
    private readonly ISystemConfigurationRepository _configuration;
    private readonly ITimeNormalizer _time;

    public GetLunchBoardQueryHandler(
        IAttendanceRepository records,
        IEmployeeRepository employees,
        IBranchRepository branches,
        IDepartmentRepository departments,
        IShiftRepository shifts,
        IShiftRosterRepository rosters,
        IDeviceRepository devices,
        ISystemConfigurationRepository configuration,
        ITimeNormalizer time)
    {
        _records = records;
        _employees = employees;
        _branches = branches;
        _departments = departments;
        _shifts = shifts;
        _rosters = rosters;
        _devices = devices;
        _configuration = configuration;
        _time = time;
    }

    public async Task<IReadOnlyList<LunchBoardEntryDto>> Handle(GetLunchBoardQuery request, CancellationToken cancellationToken)
    {
        var config = await _configuration.GetConfigurationAsync(cancellationToken);
        var dayPolicy = config?.LunchPolicy ?? LunchPolicy.Default;
        var nightPolicy = config?.NightLunchPolicy ?? LunchPolicy.DefaultNight;
        var defaultZone = config?.DefaultTimeZoneId;

        // Hoy y ayer (fecha laboral): cubre la jornada nocturna que empezó ayer.
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(_time.ToCivilTime(nowUtc, defaultZone));
        var punches = (await _records.GetByDateRangeAsync(today.AddDays(-1), today.AddDays(1), cancellationToken: cancellationToken))
            .Where(r => !r.IsVoided)
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        if (punches.Count == 0) return Array.Empty<LunchBoardEntryDto>();

        var employees = (await _employees.GetAllAsync(cancellationToken)).Where(e => punches.ContainsKey(e.Id)).ToList();
        if (employees.Count == 0) return Array.Empty<LunchBoardEntryDto>();

        var branches = (await _branches.GetAllAsync(cancellationToken)).ToDictionary(b => b.Id);
        var departments = (await _departments.GetAllAsync(cancellationToken)).ToDictionary(d => d.Id, d => d.Name);
        var shifts = (await _shifts.GetAllAsync(cancellationToken)).ToDictionary(s => s.Id);
        var deviceNames = (await _devices.GetAllDevicesAsync(cancellationToken)).ToDictionary(d => d.Id, d => d.Name);
        var rosters = (await _rosters.GetByDateRangeAsync(today.AddDays(-1).ToDateTime(TimeOnly.MinValue), today.ToDateTime(TimeOnly.MinValue), cancellationToken: cancellationToken))
            .GroupBy(r => (r.EmployeeId, r.Date.Date))
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<LunchBoardEntryDto>();
        foreach (var employee in employees)
        {
            branches.TryGetValue(employee.BranchId, out var branch);
            var zone = branch?.TimeZoneId ?? defaultZone;
            var nowCivil = _time.ToCivilTime(nowUtc, zone);
            if (!employee.IsActiveOn(nowCivil.Date)) continue;

            var records = punches[employee.Id];
            var civil = records
                .Select(r => new LunchBoardDetector.Punch(r.GetCivilTime(zone), (r.OriginalCheckType ?? r.CheckType) == CheckType.BreakStart, r))
                .ToList();
            var jornada = LunchBoardDetector.CurrentJornada(civil, nowCivil, dayPolicy.DoubleTapMinutes);
            if (jornada.Count == 0) continue;

            var shift = ResolveShift(employee, DateOnly.FromDateTime(jornada[0].Civil), shifts, rosters);
            var policy = (shift?.ShiftType == ShiftType.Nocturno ? nightPolicy : dayPolicy).ForShift(shift?.PaidLunchMinutes);
            var shiftEnd = shift == null ? (DateTime?)null : LunchBoardDetector.ShiftEnd(shift.StartTime,
                // Turno partido: la jornada termina con el segundo bloque.
                shift.ShiftType == ShiftType.Partido && shift.SecondBlockEndTime is { } secondEnd ? secondEnd : shift.EndTime,
                jornada[0].Civil);

            var lunch = LunchBoardDetector.Detect(jornada, nowCivil, policy, shiftEnd);
            if (lunch == null) continue;

            var record = (AttendanceRecord)lunch.Source!;
            result.Add(new LunchBoardEntryDto(
                employee.Id.Value,
                employee.GetFullName(),
                branch?.Name,
                departments.GetValueOrDefault(employee.DepartmentId),
                shift?.Name,
                string.IsNullOrWhiteSpace(employee.Photo) ? null : employee.Photo,
                lunch.Civil,
                nowCivil,
                policy.PaidMinutes,
                deviceNames.GetValueOrDefault(record.DeviceId)));
        }

        return result.OrderBy(r => r.LunchStartCivil).ToList();
    }

    /// <summary>Turno del día: el roster manda; si no hay, el horario del empleado (un mixto, el de ese día).</summary>
    private static Shift? ResolveShift(Employee employee, DateOnly date, IReadOnlyDictionary<ShiftId, Shift> shifts,
        IReadOnlyDictionary<(EmployeeId, DateTime), ShiftRoster> rosters)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        var shiftId = rosters.TryGetValue((employee.Id, day), out var roster) && !roster.IsRestDay && roster.ShiftId != null
            ? roster.ShiftId
            : employee.ScheduleId;
        if (shiftId == null || !shifts.TryGetValue(shiftId, out var shift)) return null;
        if (shift.ShiftType != ShiftType.Mixto) return shift;

        var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == day.DayOfWeek);
        if (dayConfig == null) return shift;
        return dayConfig.DayShiftId is { } dayShiftId && shifts.TryGetValue(dayShiftId, out var dayShift)
            ? dayShift
            : shift.EffectiveFor(dayConfig);
    }
}

/// <summary>
/// Reglas del tablero de comidas, sobre las horas civiles de los marcajes vigentes de un empleado:
/// <list type="bullet">
/// <item>Los toques dobles (a <see cref="LunchPolicy.DoubleTapMinutes"/> o menos del anterior) cuentan como uno.</item>
/// <item>La jornada en curso son los marcajes después del último hueco de <see cref="JornadaGapHours"/> horas o más.</item>
/// <item>Con un número par de marcajes el último es una salida sin regreso. Es la comida si es la primera ausencia de la
/// jornada que la política tomaría como comida: con la ventana activa, si una ausencia de los minutos con goce (o lo que
/// ya lleva) se traslaparía con la ventana; apagada, la primera ausencia de más de un toque doble. Una checada marcada
/// en el reloj como inicio de descanso es comida siempre que sea la primera.</item>
/// <item>No es comida si salió a <see cref="EndOfShiftMarginMinutes"/> minutos o menos del fin del turno (es su salida),
/// ni si lleva más de <see cref="MaxLunchHours"/> horas fuera (ya no regresó).</item>
/// </list>
/// </summary>
public static class LunchBoardDetector
{
    public const int JornadaGapHours = 6;
    public const int MaxLunchHours = 4;
    public const int EndOfShiftMarginMinutes = 45;

    public sealed record Punch(DateTime Civil, bool IsBreakStart = false, object? Source = null);

    /// <summary>Marcajes de la jornada en curso, ordenados y sin toques dobles; vacía si no hay marcajes recientes.</summary>
    public static IReadOnlyList<Punch> CurrentJornada(IEnumerable<Punch> punches, DateTime nowCivil, int doubleTapMinutes)
    {
        var ordered = punches
            .Where(p => p.Civil <= nowCivil.AddMinutes(5) && p.Civil >= nowCivil.AddHours(-24))
            .OrderBy(p => p.Civil)
            .ToList();

        var kept = new List<Punch>();
        foreach (var punch in ordered)
        {
            if (kept.Count > 0 && (punch.Civil - kept[^1].Civil).TotalMinutes <= doubleTapMinutes) continue;
            kept.Add(punch);
        }

        var start = 0;
        for (var i = 1; i < kept.Count; i++)
            if ((kept[i].Civil - kept[i - 1].Civil).TotalHours >= JornadaGapHours) start = i;
        // Un hueco largo desde el último marcaje hasta ahora también cierra la jornada.
        if (kept.Count > 0 && (nowCivil - kept[^1].Civil).TotalHours >= JornadaGapHours) return Array.Empty<Punch>();
        return kept.Skip(start).ToList();
    }

    /// <summary>Fin civil del turno de la jornada que empezó con <paramref name="firstPunch"/> (cruza medianoche si fin ≤ inicio).</summary>
    public static DateTime ShiftEnd(TimeSpan start, TimeSpan end, DateTime firstPunch)
    {
        // La entrada puede checarse poco antes de medianoche o justo después: se toma el inicio más cercano.
        var startCivil = new[] { -1, 0, 1 }
            .Select(d => firstPunch.Date.AddDays(d).Add(start))
            .OrderBy(s => Math.Abs((s - firstPunch).Ticks))
            .First();
        var length = end > start ? end - start : end - start + TimeSpan.FromDays(1);
        return startCivil + length;
    }

    /// <summary>La salida a comer en curso de la jornada, o null si no está en comida.</summary>
    public static Punch? Detect(IReadOnlyList<Punch> jornada, DateTime nowCivil, LunchPolicy policy, DateTime? shiftEndCivil)
    {
        if (jornada.Count < 2 || jornada.Count % 2 != 0) return null;

        var exit = jornada[^1];
        if (nowCivil - exit.Civil > TimeSpan.FromHours(MaxLunchHours)) return null;
        if (shiftEndCivil is { } end && exit.Civil >= end.AddMinutes(-EndOfShiftMarginMinutes)) return null;

        // Ausencias ya cerradas de la jornada: salida (impar) y regreso (par siguiente).
        var completed = new List<(Punch Exit, DateTime ExitCivil, Punch? Return, DateTime? ReturnCivil)>();
        for (var i = 1; i + 1 < jornada.Count - 1; i += 2)
            completed.Add((jornada[i], jornada[i].Civil, jornada[i + 1], jornada[i + 1].Civil));
        var lunchTaken = policy.WindowEnabled
            ? policy.Classify(completed).Contains(IntermediateKind.Lunch)
            : completed.Any(p => (p.ReturnCivil!.Value - p.ExitCivil).TotalMinutes > policy.DoubleTapMinutes);
        if (lunchTaken) return null;

        if (exit.IsBreakStart || !policy.WindowEnabled) return exit;

        var projectedReturn = Max(nowCivil, exit.Civil.AddMinutes(Math.Max(policy.PaidMinutes, policy.DoubleTapMinutes + 1)));
        return policy.Overlaps(exit.Civil, projectedReturn) ? exit : null;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
