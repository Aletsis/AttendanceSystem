namespace AttendanceSystem.Application.Abstractions;

public interface ITimeNormalizer
{
    /// <summary>
    /// Convierte una fecha y hora local/civil a UTC de forma segura, resolviendo zonas horarias,
    /// brechas de cambio de horario (adelanto de hora) y horas ambiguas (retroceso de hora).
    /// </summary>
    DateTime ToUtc(DateTime localOrUnspecifiedTime, string? timeZoneId = null);

    /// <summary>
    /// Convierte un instante UTC a fecha civil / laboral local para la zona indicada.
    /// </summary>
    DateOnly ToWorkDate(DateTime utcDateTime, string? timeZoneId = null);

    /// <summary>
    /// Convierte un instante UTC a hora civil local para la zona indicada.
    /// </summary>
    DateTime ToCivilTime(DateTime utcDateTime, string? timeZoneId = null);

    /// <summary>
    /// Resuelve el TimeZoneInfo correspondiente al id dado, o la zona por defecto del sistema/sucursal.
    /// </summary>
    TimeZoneInfo ResolveTimeZone(string? timeZoneId = null);

    /// <summary>
    /// Construye una ventana temporal semiabierta [InicioUtc, FinUtc) que cubre exactamente
    /// los días civiles [startDate, endDate] en la zona horaria indicada.
    /// </summary>
    (DateTime StartUtc, DateTime EndUtc) GetCivilWindowInUtc(DateOnly startDate, DateOnly endDate, string? timeZoneId = null);
}

public sealed class DefaultTimeNormalizer : ITimeNormalizer
{
    public static readonly ITimeNormalizer Instance = new DefaultTimeNormalizer();

    public DateTime ToUtc(DateTime localOrUnspecifiedTime, string? timeZoneId = null)
    {
        if (localOrUnspecifiedTime.Kind == DateTimeKind.Utc)
        {
            return localOrUnspecifiedTime;
        }

        var tz = ResolveTimeZone(timeZoneId);
        var unspecified = DateTime.SpecifyKind(localOrUnspecifiedTime, DateTimeKind.Unspecified);

        if (tz.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    public DateOnly ToWorkDate(DateTime utcDateTime, string? timeZoneId = null) =>
        DateOnly.FromDateTime(ToCivilTime(utcDateTime, timeZoneId));

    public DateTime ToCivilTime(DateTime utcDateTime, string? timeZoneId = null)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public TimeZoneInfo ResolveTimeZone(string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) timeZoneId = "America/Mexico_City";
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var winId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
            }
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); } catch { }
            }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); }
            catch
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId("America/Mexico_City", out var defaultWin))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(defaultWin); } catch { }
                }
                return TimeZoneInfo.Utc;
            }
        }
    }

    public (DateTime StartUtc, DateTime EndUtc) GetCivilWindowInUtc(DateOnly startDate, DateOnly endDate, string? timeZoneId = null)
    {
        var startLocal = startDate.ToDateTime(TimeOnly.MinValue);
        var endLocal = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (ToUtc(startLocal, timeZoneId), ToUtc(endLocal, timeZoneId));
    }
}
