using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Common;

/// <summary>Extras de una jornada: excedente bruto y efectivo (autorizado, redondeado y con topes).</summary>
public readonly record struct OvertimeResult(int Raw, int Effective);

/// <summary>
/// Motor único de horas extra efectivas (F06-06 / F06-07 / F06-09). Reglas:
/// <list type="number">
/// <item>Bruto: bolsa diaria = excedente calculado por la jornada (<see cref="DailyAttendance.RawOvertimeMinutes"/>);
/// bolsa semanal = lo trabajado por encima de la meta semanal desde el día en que se cruza (D-01: excedente del día).</item>
/// <item>Autorización: la de cada jornada (<see cref="DailyAttendance.OvertimeAuthorized"/>, que ya incluye los overrides
/// manuales), no la bandera global del empleado.</item>
/// <item>Redondeo: una sola vez, sobre el bruto, con el método del empleado.</item>
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
        Func<DateTime, DateTime>? periodStartOf = null)
    {
        var result = new Dictionary<DailyAttendanceId, OvertimeResult>();
        var sorted = attendances.OrderBy(a => a.Date).ThenBy(a => a.Id.Value).ToList();
        periodStartOf ??= date => GetWeekStart(date, weeklyStartDay);

        var accumulatedByPeriod = new Dictionary<DateTime, double>();
        var workedByWeek = new Dictionary<DateTime, double>();

        foreach (var att in sorted)
        {
            double raw;
            if (att.WeeklyWorkHours is { } weekly && weekly > TimeSpan.Zero)
            {
                var week = GetWeekStart(att.Date, weeklyStartDay);
                var prior = workedByWeek.TryGetValue(week, out var w) ? w : 0;
                var worked = WorkedMinutes(att);
                workedByWeek[week] = prior + worked;

                var goal = weekly.TotalMinutes;
                raw = prior >= goal ? worked : Math.Max(0, prior + worked - goal);
            }
            else
            {
                raw = Math.Max(0, att.RawOvertimeMinutes);
            }

            double effective = 0;
            if (raw > 0 && att.OvertimeAuthorized)
            {
                effective = DailyAttendance.ApplyOvertimeRounding(raw, employee.OvertimeCalculationMethod);

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

            result[att.Id] = new OvertimeResult((int)raw, (int)effective);
        }

        return result;
    }

    private static double WorkedMinutes(DailyAttendance att)
    {
        int workedToday = att.TotalWorkedMinutes;
        if (workedToday <= 0 && att.ActualCheckIn.HasValue && att.ActualCheckOut.HasValue)
        {
            DateTime refIn = att.GetReferenceEntry() ?? att.ActualCheckIn.Value;
            DateTime refOut = att.GetReferenceExit() ?? att.ActualCheckOut.Value;
            double span = (refOut - refIn).TotalMinutes - att.LunchBreakMinutesApplied;
            if (att.TemporaryExitStatus == TemporaryExitStatus.ApprovedUnpaid)
            {
                span -= att.TemporaryExitMinutes;
            }
            workedToday = (int)Math.Max(0, span);
        }
        return workedToday;
    }
}
