namespace AttendanceSystem.Application.Common;

/// <summary>Zonas horarias que se ofrecen al configurar un reloj checador (identificadores IANA).</summary>
public static class TimeZoneCatalog
{
    public sealed record Entry(string Id, string Name);

    public static readonly IReadOnlyList<Entry> Common = new[]
    {
        new Entry("America/Mexico_City", "México – Centro"),
        new Entry("America/Monterrey", "México – Monterrey"),
        new Entry("America/Merida", "México – Mérida"),
        new Entry("America/Cancun", "México – Quintana Roo"),
        new Entry("America/Chihuahua", "México – Chihuahua"),
        new Entry("America/Ciudad_Juarez", "México – Ciudad Juárez"),
        new Entry("America/Matamoros", "México – Frontera noreste"),
        new Entry("America/Mazatlan", "México – Pacífico"),
        new Entry("America/Hermosillo", "México – Sonora"),
        new Entry("America/Tijuana", "México – Baja California"),
        new Entry("America/New_York", "EE. UU. – Este"),
        new Entry("America/Chicago", "EE. UU. – Centro"),
        new Entry("America/Denver", "EE. UU. – Montaña"),
        new Entry("America/Phoenix", "EE. UU. – Arizona"),
        new Entry("America/Los_Angeles", "EE. UU. – Pacífico"),
        new Entry("America/Guatemala", "Centroamérica"),
        new Entry("America/Bogota", "Colombia / Perú"),
        new Entry("UTC", "UTC"),
    };

    /// <summary>Si el sistema conoce la zona (IANA o Windows).</summary>
    public static bool IsValid(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return false;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out _)) return true;
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId.Trim(), out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out _);
    }

    /// <summary>Texto para mostrar: nombre del catálogo (o el id) con su desfase actual.</summary>
    public static string Describe(string timeZoneId, DateTime? utcNow = null)
    {
        var name = Common.FirstOrDefault(e => e.Id == timeZoneId)?.Name ?? timeZoneId;
        if (!IsValid(timeZoneId)) return name;
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) ? found
            : TimeZoneInfo.FindSystemTimeZoneById(TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var w) ? w : timeZoneId);
        var offset = zone.GetUtcOffset(utcNow ?? DateTime.UtcNow);
        return $"{name} (UTC{(offset < TimeSpan.Zero ? "−" : "+")}{offset.Duration():hh\\:mm})";
    }
}
