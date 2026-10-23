using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Common;

/// <summary>
/// Extras de una jornada: excedente bruto y efectivo (autorizado, redondeado y con topes), y el faltante de la meta
/// semanal que se reporta en el último día con bolsa de una semana terminada (caso 6 f).
/// </summary>
public readonly record struct OvertimeResult(int Raw, int Effective, int WeeklyShortfall = 0);

/// <summary>
/// Cómo se esperaba un día sin jornada calculada (futuro de la semana o fuera del período de empleo):
/// <c>Workable</c> = laborable según los descansos del empleado; <c>Employed</c> = dentro de un período de empleo;
/// con bolsa semanal, o minutos programados si es diario.
/// </summary>
public readonly record struct ExpectedDay(bool Workable, bool Employed, bool WeeklyBag, int ScheduledMinutes);

/// <summary>
/// Motor único de horas extra efectivas (F06-06 / F06-07 / F06-09). Reglas:
/// <list type="number">
/// <item>Bruto: bolsa diaria = excedente calculado por la jornada (<see cref="DailyAttendance.RawOvertimeMinutes"/>);
/// bolsa semanal = lo trabajado por encima de la meta semanal desde el día en que se cruza (D-01: excedente del día).</item>
/// <item>Semana partida (decisión de cierre F06-06): la meta de la bolsa es proporcional a los días laborables del
/// empleado que estuvieron con bolsa (meta × días con bolsa ÷ días laborables). Si la semana mezcla bolsa y horario
/// diario, todo lo trabajado en sus días laborables se compara con la meta combinada (bolsa proporcional + minutos
/// programados de los días diarios) y el faltante de una parte compensa la otra. Faltas y permisos no reducen la meta;
/// los descansos trabajados conservan su propio excedente.</item>
/// <item>Autorización: la de cada jornada (<see cref="DailyAttendance.OvertimeAuthorized"/>, que ya incluye los overrides
/// manuales), no la bandera global del empleado.</item>
/// <item>Redondeo: una sola vez, sobre el bruto, con el método del empleado; una parte autorizada por RH (caso 12)
/// sustituye al redondeo.</item>
/// <item>Tope diario: por jornada. Tope de período: acumulado dentro del período contable que contiene la jornada
/// (<c>periodStartOf</c>), en orden cronológico; no depende del rango solicitado si se le entregan todas las jornadas
/// del período.</item>
/// </list>
/// El resultado se guarda en la jornada; los reportes lo leen y no vuelven a calcularlo.
/// </summary>
public static class WeeklyHoursCalculationHelper
{
    /// <summary>
    /// Obtiene la fecha de inicio de la semana laboral que contiene a la fecha indicada.
    /// </summary>
    public static DateTime GetWeekStart(DateTime date, DayOfWeek weeklyStartDay)
    {
        int diff = (7 + (int)date.DayOfWeek - (int)weeklyStartDay) % 7;
        return date.Date.AddDays(-diff);
    }

    /// <summary>
    /// Obtiene la fecha de fin de la semana laboral (6 días después del inicio).
    /// </summary>
    public static DateTime GetWeekEnd(DateTime date, DayOfWeek weeklyStartDay)
    {
        return GetWeekStart(date, weeklyStartDay).AddDays(6);
    }

    /// <summary>Extras efectivas por jornada (compatibilidad); ver <see cref="Calculate"/>.</summary>
    public static Dictionary<DailyAttendanceId, int> CalculateEffectiveOvertime(
        IEnumerable<DailyAttendance> attendances,
        Employee employee,
        DayOfWeek weeklyStartDay,
        Func<DateTime, DateTime>? periodStartOf = null) =>
        Calculate(attendances, employee, weeklyStartDay, periodStartOf).ToDictionary(kv => kv.Key, kv => kv.Value.Effective);

    public static Dictionary<DailyAttendanceId, OvertimeResult> Calculate(
        IEnumerable<DailyAttendance> attendances,
        Employee employee,
        DayOfWeek weeklyStartDay,
        Func<DateTime, DateTime>? periodStartOf = null,
        Func<DateTime, ExpectedDay>? expectedDay = null)
    {
        var result = new Dictionary<DailyAttendanceId, OvertimeResult>();
        var sorted = attendances.OrderBy(a => a.Date).ThenBy(a => a.Id.Value).ToList();
        periodStartOf ??= date => GetWeekStart(date, weeklyStartDay);

        var rawById = WeeklyRaw(sorted, weeklyStartDay, expectedDay, out var shortfallById);
        var accumulatedByPeriod = new Dictionary<DateTime, double>();

        foreach (var att in sorted)
        {
            double raw = rawById[att.Id];

            double effective = 0;
            if (raw > 0 && att.OvertimeAuthorized)
            {
                effective = att.AuthorizedOvertime(raw, employee.OvertimeCalculationMethod);

                if (employee.OvertimeCapType == OvertimeCapType.Daily && employee.OvertimeCapMinutes.HasValue)
                {
                    effective = Math.Min(effective, employee.OvertimeCapMinutes.Value);
                }

                if (employee.OvertimeCapType == OvertimeCapType.Period && employee.OvertimeCapMinutes.HasValue)
                {
                    var period = periodStartOf(att.Date.Date);
                    var used = accumulatedByPeriod.TryGetValue(period, out var u) ? u : 0;
                    effective = Math.Min(effective, Math.Max(0, employee.OvertimeCapMinutes.Value - used));
                    accumulatedByPeriod[period] = used + effective;
                }
            }

            result[att.Id] = new OvertimeResult((int)raw, (int)effective, shortfallById.GetValueOrDefault(att.Id));
        }

        return result;
    }

    private static bool IsWeeklyBag(DailyAttendance att) => att.WeeklyWorkHours is { } w && w > TimeSpan.Zero;

    /// <summary>Excedente bruto por jornada, semana por semana (bolsa, diario o semana partida).</summary>
    private static Dictionary<DailyAttendanceId, double> WeeklyRaw(
        IReadOnlyList<DailyAttendance> sorted,
        DayOfWeek weeklyStartDay,
        Func<DateTime, ExpectedDay>? expectedDay,
        out Dictionary<DailyAttendanceId, int> shortfall)
    {
        var raw = new Dictionary<DailyAttendanceId, double>();
        shortfall = new Dictionary<DailyAttendanceId, int>();
        foreach (var week in sorted.GroupBy(a => GetWeekStart(a.Date, weeklyStartDay)))
        {
            var days = week.ToList();
            var bagDay = days.FirstOrDefault(IsWeeklyBag);
            if (bagDay == null)
            {
                foreach (var att in days) raw[att.Id] = Math.Max(0, att.RawOvertimeMinutes);
                continue;
            }

            // Días de la semana: los calculados según su instantánea; los demás según lo esperado. El divisor son los
            // días laborables normales del empleado; el período de empleo sólo afecta a los días con bolsa.
            int workable = 0, bagWorkable = 0, dailyWorkable = 0;
            double dailyScheduled = 0;
            for (int i = 0; i < 7; i++)
            {
                var date = week.Key.AddDays(i);
                var att = days.FirstOrDefault(a => a.Date.Date == date);
                ExpectedDay info = att != null
                    ? new ExpectedDay(!att.IsRestDay, true, IsWeeklyBag(att), IsWeeklyBag(att) ? 0 : ScheduledMinutes(att))
                    : expectedDay?.Invoke(date) ?? default;
                if (!info.Workable) continue;
                workable++;
                if (!info.Employed) continue;
                if (info.WeeklyBag) bagWorkable++;
                else
                {
                    dailyWorkable++;
                    dailyScheduled += info.ScheduledMinutes;
                }
            }

            var fullGoal = bagDay.WeeklyWorkHours!.Value.TotalMinutes;
            var bagGoal = workable > 0 ? fullGoal * bagWorkable / workable : fullGoal;
            var mixed = dailyWorkable > 0;

            // Semana sólo de bolsa: los días de bolsa contra la meta (proporcional si hubo días fuera de empleo).
            // Semana partida: todos los días laborables contra la meta combinada.
            var goal = mixed ? bagGoal + dailyScheduled : bagGoal;
            double cumulative = 0;
            foreach (var att in days)
            {
                var accumulates = mixed ? !att.IsRestDay : IsWeeklyBag(att);
                if (!accumulates)
                {
                    raw[att.Id] = Math.Max(0, att.RawOvertimeMinutes);
                    continue;
                }

                var worked = WorkedMinutes(att);
                var prior = cumulative;
                cumulative += worked;
                raw[att.Id] = prior >= goal ? worked : Math.Max(0, prior + worked - goal);
            }

            // Caso 6 (f): en una semana sólo de bolsa ya terminada (calculada hasta su último día), lo que faltó para
            // la meta se reporta en su último día con bolsa. Las semanas partidas ya reportan el faltante diario.
            var weekEnd = week.Key.AddDays(6);
            var lastBagDay = days.LastOrDefault(IsWeeklyBag);
            if (!mixed && lastBagDay != null && days.Any(a => a.Date.Date == weekEnd) && cumulative < goal)
            {
                shortfall[lastBagDay.Id] = (int)Math.Round(goal - cumulative);
            }
        }
        return raw;
    }

    /// <summary>Minutos programados de una jornada con horario diario (duración del turno).</summary>
    private static int ScheduledMinutes(DailyAttendance att)
    {
        if (att.WorkHours is { } hours && hours > TimeSpan.Zero) return (int)hours.TotalMinutes;
        if (att.ScheduledCheckIn is { } start && att.ScheduledCheckOut is { } end)
        {
            var span = end >= start ? end - start : end.Add(TimeSpan.FromDays(1)) - start;
            return (int)span.TotalMinutes;
        }
        return 0;
    }

    /// <summary>Tiempo trabajado guardado por la jornada (única fuente).</summary>
    private static double WorkedMinutes(DailyAttendance att) => att.TotalWorkedMinutes;
}
