namespace AttendanceSystem.Application.Common;

/// <summary>Desfase horario y comandos de reloj en el formato del protocolo ADMS (TimeZone/TZ en horas enteras).</summary>
public static class AdmsTimeZone
{
    public static string Format(TimeSpan utcOffset)
        => ((int)Math.Round(utcOffset.TotalHours, MidpointRounding.AwayFromZero)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Comandos que fijan la zona del reloj (sin horario de verano) y su hora civil. El reloj calcula su hora con el
    /// encabezado Date (UTC) más este desfase, así que ambos deben corresponder a la misma zona.
    /// </summary>
    public static IReadOnlyList<string> ClockCommands(DateTime civilTime, TimeSpan utcOffset)
    {
        var offset = Format(utcOffset);
        return new[]
        {
            $"SET OPTIONS TimeZone={offset},TZ={offset},~TimeZone={offset},DaylightSavingTime=0,~DaylightSavingTime=0",
            $"SET OPTIONS DateTime={civilTime:yyyy-MM-dd HH:mm:ss}",
        };
    }
}
