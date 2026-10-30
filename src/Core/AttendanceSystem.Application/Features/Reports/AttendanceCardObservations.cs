using System.Globalization;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Reports;

/// <summary>Tipos de observación que se pueden imprimir en la tarjeta de asistencia.</summary>
[Flags]
public enum CardObservationKinds
{
    None = 0,
    EarlyDeparture = 1,
    TemporaryExit = 2,
    Absence = 4,
    MissingCheckIn = 8,
    MissingCheckOut = 16,
    All = EarlyDeparture | TemporaryExit | Absence | MissingCheckIn | MissingCheckOut
}

/// <summary>
/// Observaciones de la tarjeta de asistencia: solo lo que el empleado firma (faltas, checadas faltantes, salidas).
/// Los avisos para RH de <see cref="DailyAttendance.Notes"/> (sin turno, extras sin autorizar, incidencias) no se imprimen.
/// </summary>
public static class AttendanceCardObservations
{
    public static readonly CultureInfo Culture = new("es-MX");

    /// <summary>Un renglón por día con observaciones, en orden de fecha: "lun 19/10: Falta · Sin salida".</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<AttendanceReportViewDto> days, CardObservationKinds kinds) =>
        days.OrderBy(d => d.Date)
            .Select(d => (d.Date, Items: For(d, kinds)))
            .Where(x => x.Items.Count > 0)
            .Select(x => $"{x.Date.ToString("ddd dd/MM", Culture)}: {string.Join(" · ", x.Items)}")
            .ToList();

    public static IReadOnlyList<string> For(AttendanceReportViewDto day, CardObservationKinds kinds)
    {
        var items = new List<string>();

        if (day.IsAbsent)
        {
            // Una falta no tiene checadas: no se repite como entrada y salida faltantes.
            if (kinds.HasFlag(CardObservationKinds.Absence)) items.Add("Falta");
            return items;
        }

        var split = day.ShiftType == ShiftType.Partido;
        if (kinds.HasFlag(CardObservationKinds.MissingCheckIn))
        {
            if (day.MissingCheckIn) items.Add(split ? "Sin entrada (bloque 1)" : "Sin entrada");
            if (split && day.MissingBlock2CheckIn) items.Add("Sin entrada (bloque 2)");
        }
        if (kinds.HasFlag(CardObservationKinds.MissingCheckOut))
        {
            if (split && day.MissingBlock1CheckOut) items.Add("Sin salida (bloque 1)");
            if (day.MissingCheckOut) items.Add(split ? "Sin salida (bloque 2)" : "Sin salida");
        }
        if (kinds.HasFlag(CardObservationKinds.EarlyDeparture) && day.EarlyDepartureMinutes > 0)
            items.Add($"Salida anticipada de {day.EarlyDepartureMinutes} min");
        if (kinds.HasFlag(CardObservationKinds.TemporaryExit))
            items.AddRange(TemporaryExits(day));

        return items;
    }

    // Misma redacción que las notas de la jornada; la comida y las checadas dobles ignoradas no son salidas.
    private static IEnumerable<string> TemporaryExits(AttendanceReportViewDto day)
    {
        if (day.TemporaryExits.Count > 0)
        {
            foreach (var exit in day.TemporaryExits)
            {
                if (Classification(exit.Status) is { } classification)
                    yield return $"Salida temporal {exit.ExitTime:HH:mm}–{exit.ReturnTime:HH:mm} ({exit.Minutes} min) — {classification}";
            }
        }
        else if (day.HasTemporaryExits && Classification((TemporaryExitStatus)day.TemporaryExitStatus) is { } classification)
        {
            // Jornadas anteriores al detalle por salida.
            yield return $"Salida temporal de {day.TemporaryExitMinutes} min — {classification}";
        }
    }

    private static string? Classification(TemporaryExitStatus status) => status switch
    {
        TemporaryExitStatus.Pending => "pendiente de clasificar",
        TemporaryExitStatus.ApprovedPaid => "permiso con goce",
        TemporaryExitStatus.ApprovedUnpaid => "permiso sin goce",
        _ => null
    };
}
