namespace AttendanceSystem.Domain.Services;

using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;

/// <summary>
/// Período contable estable que contiene una fecha (F06-07), según la configuración del sistema:
/// semanal (desde WeeklyStartDay), quincenal (FortnightFirstDay / FortnightSecondDay) o mensual
/// (MonthlyStartDay). Es la frontera del tope de extras <c>OvertimeCapType.Period</c>: el acumulado se
/// reinicia al iniciar el período, nunca según el rango que el usuario eligió recalcular.
/// </summary>
public static class WorkPeriodCalculator
{
    public static (DateTime Start, DateTime End) Containing(DateTime date, SystemConfiguration? configuration) =>
        Containing(
            date,
            configuration?.WorkPeriodMode ?? WorkPeriodMode.Weekly,
            configuration?.WeeklyStartDay ?? DayOfWeek.Monday,
            configuration?.FortnightFirstDay ?? 1,
            configuration?.FortnightSecondDay ?? 16,
            configuration?.MonthlyStartDay ?? 1);

    public static (DateTime Start, DateTime End) Containing(
        DateTime date,
        WorkPeriodMode mode,
        DayOfWeek weeklyStartDay,
        int fortnightFirstDay,
        int fortnightSecondDay,
        int monthlyStartDay)
    {
        var day = date.Date;
        switch (mode)
        {
            case WorkPeriodMode.Monthly:
            {
                var start = MonthDay(day.Year, day.Month, monthlyStartDay);
                if (start > day) start = MonthDay(day.AddMonths(-1).Year, day.AddMonths(-1).Month, monthlyStartDay);
                var next = MonthDay(start.AddMonths(1).Year, start.AddMonths(1).Month, monthlyStartDay);
                return (start, next.AddDays(-1));
            }
            case WorkPeriodMode.Fortnightly:
            {
                var first = MonthDay(day.Year, day.Month, fortnightFirstDay);
                var second = MonthDay(day.Year, day.Month, fortnightSecondDay);
                if (day >= second)
                {
                    var nextMonth = day.AddMonths(1);
                    return (second, MonthDay(nextMonth.Year, nextMonth.Month, fortnightFirstDay).AddDays(-1));
                }
                if (day >= first)
                {
                    return (first, second.AddDays(-1));
                }
                var previous = day.AddMonths(-1);
                return (MonthDay(previous.Year, previous.Month, fortnightSecondDay), first.AddDays(-1));
            }
            default:
            {
                int diff = (7 + (int)day.DayOfWeek - (int)weeklyStartDay) % 7;
                var start = day.AddDays(-diff);
                return (start, start.AddDays(6));
            }
        }
    }

    private static DateTime MonthDay(int year, int month, int day) =>
        new(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));
}
