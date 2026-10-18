using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Common;

/// <summary>
/// Provee la lógica de acumulación y cómputo de horas para la Bolsa de Horas Semanal
/// apoyándose en los períodos semanales y el día de inicio de semana (WeeklyStartDay).
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

    /// <summary>
    /// Calcula el tiempo extra efectivo para cada día de un empleado,
    /// soportando tanto turnos con bolsa diaria como turnos con bolsa semanal.
    /// Para bolsa semanal, acumula las horas laboradas en la semana (desde weeklyStartDay);
    /// cualquier excedente sobre la meta semanal (WeeklyWorkHours) genera horas extras
    /// en el día en que se cruza la meta y en los días posteriores de esa semana.
    /// </summary>
    public static Dictionary<DailyAttendanceId, int> CalculateEffectiveOvertime(
        IEnumerable<DailyAttendance> attendances,
        Employee employee,
        DayOfWeek weeklyStartDay)
    {
        var result = new Dictionary<DailyAttendanceId, int>();
        var sorted = attendances.OrderBy(a => a.Date).ToList();

        // Agrupar registros por semana de corte según weeklyStartDay
        var weeklyGroups = sorted.GroupBy(a => GetWeekStart(a.Date, weeklyStartDay));

        double accumulatedPeriodOvertime = 0;

        foreach (var weekGroup in weeklyGroups)
        {
            var weekRecords = weekGroup.OrderBy(a => a.Date).ToList();

            // Identificar si los registros de la semana tienen turno de bolsa semanal
            // Si hay registros con WeeklyWorkHours > 0, se aplica el acumulador semanal
            double accumulatedWorkedMinutesInWeek = 0;

            foreach (var att in weekRecords)
            {
                bool isWeeklyShift = att.WeeklyWorkHours.HasValue && att.WeeklyWorkHours.Value > TimeSpan.Zero;

                int workedToday = att.TotalWorkedMinutes;
                if (workedToday <= 0 && att.ActualCheckIn.HasValue && att.ActualCheckOut.HasValue)
                {
                    DateTime refIn = att.GetReferenceEntry() ?? att.ActualCheckIn.Value;
                    DateTime refOut = att.GetReferenceExit() ?? att.ActualCheckOut.Value;
                    double span = (refOut - refIn).TotalMinutes;
                    span -= att.LunchBreakMinutesApplied;
                    if (att.TemporaryExitStatus == TemporaryExitStatus.ApprovedUnpaid)
                    {
                        span -= att.TemporaryExitMinutes;
                    }
                    workedToday = (int)Math.Max(0, span);
                }

                if (isWeeklyShift)
                {
                    double weeklyGoal = att.WeeklyWorkHours!.Value.TotalMinutes;
                    double priorAccumulated = accumulatedWorkedMinutesInWeek;
                    accumulatedWorkedMinutesInWeek += workedToday;

                    double rawOvertime = 0;
                    if (accumulatedWorkedMinutesInWeek > weeklyGoal)
                    {
                        if (priorAccumulated >= weeklyGoal)
                        {
                            // La meta ya se había cubierto antes de hoy; todo lo trabajado hoy es tiempo extra
                            rawOvertime = workedToday;
                        }
                        else
                        {
                            // Hoy se superó la meta semanal; solo el excedente es tiempo extra
                            rawOvertime = accumulatedWorkedMinutesInWeek - weeklyGoal;
                        }
                    }

                    double effectiveOvertime = 0;
                    if (rawOvertime > 0 && employee.OvertimeAuthorized)
                    {
                        effectiveOvertime = DailyAttendance.ApplyOvertimeRounding(rawOvertime, employee.OvertimeCalculationMethod);

                        if (employee.OvertimeCapType == OvertimeCapType.Daily && employee.OvertimeCapMinutes.HasValue)
                        {
                            effectiveOvertime = Math.Min(effectiveOvertime, employee.OvertimeCapMinutes.Value);
                        }

                        if (employee.OvertimeCapType == OvertimeCapType.Period && employee.OvertimeCapMinutes.HasValue)
                        {
                            double remaining = Math.Max(0, employee.OvertimeCapMinutes.Value - accumulatedPeriodOvertime);
                            effectiveOvertime = Math.Min(effectiveOvertime, remaining);
                            accumulatedPeriodOvertime += effectiveOvertime;
                        }
                    }

                    result[att.Id] = (int)effectiveOvertime;
                }
                else
                {
                    // Turno diario / estándar / partido: usa el tiempo extra bruto calculado como base
                    double dailyOt = att.RawOvertimeMinutes > 0 ? att.RawOvertimeMinutes : att.OvertimeMinutes;

                    if (employee.OvertimeAuthorized && dailyOt > 0)
                    {
                        dailyOt = DailyAttendance.ApplyOvertimeRounding(dailyOt, employee.OvertimeCalculationMethod);

                        if (employee.OvertimeCapType == OvertimeCapType.Daily && employee.OvertimeCapMinutes.HasValue)
                        {
                            dailyOt = Math.Min(dailyOt, employee.OvertimeCapMinutes.Value);
                        }

                        if (employee.OvertimeCapType == OvertimeCapType.Period && employee.OvertimeCapMinutes.HasValue)
                        {
                            double remaining = Math.Max(0, employee.OvertimeCapMinutes.Value - accumulatedPeriodOvertime);
                            dailyOt = Math.Min(dailyOt, remaining);
                            accumulatedPeriodOvertime += dailyOt;
                        }
                    }
                    else if (!employee.OvertimeAuthorized)
                    {
                        dailyOt = 0;
                    }

                    result[att.Id] = (int)dailyOt;
                }
            }
        }

        return result;
    }
}
