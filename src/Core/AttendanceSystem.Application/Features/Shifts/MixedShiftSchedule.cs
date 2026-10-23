using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Application.Features.Shifts;

/// <summary>
/// Reglas de la plantilla de un turno mixto compartidas por el servidor y las pantallas (caso 9): los 7 días llevan
/// horario (los descansos son los del empleado) y se avisa cuando un día termina demasiado cerca del siguiente.
/// </summary>
public static class MixedShiftSchedule
{
    public static readonly IReadOnlyList<DayOfWeek> Week = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    /// <summary>Días de la semana sin horario en la plantilla.</summary>
    public static IReadOnlyList<DayOfWeek> MissingDays(IEnumerable<DayOfWeek> daysWithSchedule)
    {
        var present = daysWithSchedule.ToHashSet();
        return Week.Where(d => !present.Contains(d)).ToList();
    }

    public static string MissingDaysMessage(IReadOnlyList<DayOfWeek> missing) =>
        $"Todos los días de un turno mixto deben tener horario; falta: {string.Join(", ", missing.Select(DayName))}. " +
        "Los descansos se toman de los días de descanso del empleado.";

    /// <summary>
    /// Caso 9 (b): días cuyo horario termina a menos de 2 h del inicio del día siguiente. La entrada del día siguiente
    /// se acepta desde 2 h antes, así que una salida tardía del primero se tomaría como entrada del segundo.
    /// Un flexible sin ventana de llegada no tiene hora de inicio y no se revisa.
    /// </summary>
    public static IReadOnlyList<string> AdjacencyWarnings(IReadOnlyDictionary<DayOfWeek, ShiftDto> days)
    {
        var warnings = new List<string>();
        for (int i = 0; i < Week.Count; i++)
        {
            var day = Week[i];
            var next = Week[(i + 1) % Week.Count];
            if (!days.TryGetValue(day, out var today) || !days.TryGetValue(next, out var tomorrow)) continue;
            if (End(today) is not { } end || Start(tomorrow) is not { } nextStart) continue;

            var nextStartFromToday = nextStart + TimeSpan.FromDays(1);
            if (end > nextStartFromToday - JornadaWindowPolicy.EarlyArrivalAllowance)
            {
                warnings.Add($"El {DayName(day)} ({today.Name}) termina a las {Clock(end)}{(end >= TimeSpan.FromDays(1) ? $" del {DayName(next)}" : "")} " +
                             $"y el {DayName(next)} ({tomorrow.Name}) empieza a las {Clock(nextStart)}: con menos de 2 h entre ambos, " +
                             $"una salida tardía del {DayName(day)} se tomaría como entrada del {DayName(next)}.");
            }
        }
        return warnings;
    }

    /// <summary>Inicio programado desde las 00:00 del día; null si no tiene hora de inicio.</summary>
    private static TimeSpan? Start(ShiftDto shift) =>
        shift.ShiftType == ShiftType.Flexible && !shift.HasEntryWindow ? null : shift.StartTime;

    /// <summary>Fin programado desde las 00:00 del día (más de 24 h si termina al día siguiente); null si no se conoce.</summary>
    private static TimeSpan? End(ShiftDto shift)
    {
        switch (shift.ShiftType)
        {
            case ShiftType.Partido when shift.SecondBlockStartTime is { } b2Start && shift.SecondBlockEndTime is { } b2End:
                return b2End <= b2Start ? b2End + TimeSpan.FromDays(1) : b2End;
            case ShiftType.Flexible:
                if (!shift.HasEntryWindow || shift.WorkHours <= TimeSpan.Zero) return null;
                return (shift.FlexWindowEndTime ?? shift.StartTime) + shift.WorkHours;
            default:
                return shift.WorkHours > TimeSpan.Zero ? shift.StartTime + shift.WorkHours : null;
        }
    }

    private static string Clock(TimeSpan time)
    {
        var t = TimeSpan.FromMinutes(time.TotalMinutes % (24 * 60));
        return $"{t.Hours:00}:{t.Minutes:00}";
    }

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miércoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sábado",
        _ => "domingo"
    };
}
