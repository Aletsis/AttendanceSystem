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
}
