namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Zonas recientes que algunos sistemas operativos aún no traen (p. ej. Windows con datos de zona anteriores a 2022) y
/// la zona con las mismas reglas actuales. Sin esto, un reloj de esa zona caería a la zona por defecto y quedaría
/// corrido. America/Ciudad_Juarez sigue desde 2022 la hora de montaña con horario de verano de EE. UU.
/// </summary>
public static class TimeZoneEquivalents
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Ciudad_Juarez"] = "America/Denver",
    };

    /// <summary>La zona equivalente disponible en este sistema, si la zona pedida tiene una.</summary>
    public static bool TryFind(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId) || !Map.TryGetValue(timeZoneId.Trim(), out var equivalent)) return false;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(equivalent, out var found)) { zone = found; return true; }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(equivalent, out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out found)) { zone = found; return true; }
        return false;
    }
}
