using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public class TimeNormalizer : ITimeNormalizer
{
    public const string DefaultAuthoritativeTimeZoneId = "America/Mexico_City";
    private readonly ILogger<TimeNormalizer> _logger;
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> TimeZoneCache = new(StringComparer.OrdinalIgnoreCase);

    public TimeNormalizer(ILogger<TimeNormalizer> logger)
    {
        _logger = logger;
    }

    public TimeZoneInfo ResolveTimeZone(string? timeZoneId = null)
    {
        var effectiveId = string.IsNullOrWhiteSpace(timeZoneId)
            ? DefaultAuthoritativeTimeZoneId
            : timeZoneId.Trim();

        return TimeZoneCache.GetOrAdd(effectiveId, id =>
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Intentar mapeo IANA / Windows si difiere el SO
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var winId))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
                }
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); } catch { }
                }

                _logger.LogWarning("Zona horaria '{TimeZoneId}' no encontrada en el sistema. Usando zona autoritativa por defecto '{DefaultTz}'.", id, DefaultAuthoritativeTimeZoneId);
                return GetDefaultFallbackTimeZone();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al resolver zona horaria '{TimeZoneId}'. Usando zona autoritativa por defecto '{DefaultTz}'.", id, DefaultAuthoritativeTimeZoneId);
                return GetDefaultFallbackTimeZone();
            }
        });
    }

    private static TimeZoneInfo GetDefaultFallbackTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultAuthoritativeTimeZoneId);
        }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(DefaultAuthoritativeTimeZoneId, out var winId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
            }
            return TimeZoneInfo.Utc;
        }
    }

    public DateTime ToUtc(DateTime localOrUnspecifiedTime, string? timeZoneId = null)
    {
        if (localOrUnspecifiedTime.Kind == DateTimeKind.Utc)
        {
            return localOrUnspecifiedTime;
        }

        var tz = ResolveTimeZone(timeZoneId);
        var unspecified = DateTime.SpecifyKind(localOrUnspecifiedTime, DateTimeKind.Unspecified);

        // Manejo de brecha inexistente en cambio de horario (Spring Forward)
        if (tz.IsInvalidTime(unspecified))
        {
            _logger.LogWarning("Hora inválida/brecha de horario detectada: {Time} en zona {Zone}. Ajustando 1 hora hacia adelante con trazabilidad.",
                unspecified, tz.Id);
            unspecified = unspecified.AddHours(1);
        }
        else if (tz.IsAmbiguousTime(unspecified))
        {
            _logger.LogInformation("Hora ambigua detectada por cambio de horario: {Time} en zona {Zone}. Resolviendo deterministamente con horario estándar.",
                unspecified, tz.Id);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    public DateOnly ToWorkDate(DateTime utcDateTime, string? timeZoneId = null)
    {
        var local = ToCivilTime(utcDateTime, timeZoneId);
        return DateOnly.FromDateTime(local);
    }

    public DateTime ToCivilTime(DateTime utcDateTime, string? timeZoneId = null)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        var tz = ResolveTimeZone(timeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public (DateTime StartUtc, DateTime EndUtc) GetCivilWindowInUtc(DateOnly startDate, DateOnly endDate, string? timeZoneId = null)
    {
        var localStart = startDate.ToDateTime(TimeOnly.MinValue);
        var localEndExclusive = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var startUtc = ToUtc(localStart, timeZoneId);
        var endUtc = ToUtc(localEndExclusive, timeZoneId);

        return (startUtc, endUtc);
    }
}
