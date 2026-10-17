using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public class TimeNormalizer : ITimeNormalizer
{
    private readonly ILogger<TimeNormalizer> _logger;
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> TimeZoneCache = new(StringComparer.OrdinalIgnoreCase);

    public TimeNormalizer(ILogger<TimeNormalizer> logger)
    {
        _logger = logger;
    }

    public TimeZoneInfo ResolveTimeZone(string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        return TimeZoneCache.GetOrAdd(timeZoneId, id =>
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

                _logger.LogWarning("Zona horaria '{TimeZoneId}' no encontrada en el sistema. Usando zona local por defecto.", id);
                return TimeZoneInfo.Local;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al resolver zona horaria '{TimeZoneId}'. Usando zona local por defecto.", id);
                return TimeZoneInfo.Local;
            }
        });
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
            _logger.LogInformation("Hora inválida/brecha de horario detectada: {Time} en zona {Zone}. Ajustando 1 hora hacia adelante.",
                unspecified, tz.Id);
            unspecified = unspecified.AddHours(1);
        }

        // Manejo de hora ambigua en cambio de horario (Fall Back): ConvertTimeToUtc usa horario estándar
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
}
