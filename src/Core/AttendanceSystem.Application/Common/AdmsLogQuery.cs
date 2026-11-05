using System.Globalization;

namespace AttendanceSystem.Application.Common;

/// <summary>
/// Consulta de marcajes ADMS en la sintaxis de cada familia de reloj:
/// <list type="bullet">
/// <item>Control de acceso (DeviceType=acc): <c>DATA QUERY tablename=Transaction,starttime=…,endtime=…</c>.</item>
/// <item>Asistencia (att, el valor por omisión): <c>DATA QUERY ATTLOG StartTime=…\tEndTime=…</c>. Rechazan la sintaxis
/// de acceso con -1004 (p. ej. SenseFace M2F) y responden subiendo los marcajes por <c>POST /iclock/cdata?table=ATTLOG</c>.</item>
/// </list>
/// Las fechas son la hora civil del reloj.
/// </summary>
public static class AdmsLogQuery
{
    // Ventana que cubre toda la memoria del reloj cuando se pide la descarga completa en un reloj de asistencia,
    // cuya consulta siempre lleva rango.
    private static readonly DateTime FullStart = new(2000, 1, 1);
    private static readonly DateTime FullEnd = new(2099, 12, 31, 23, 59, 59);

    public static bool IsAccess(string? deviceType) =>
        string.Equals(deviceType?.Trim(), "acc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Todos los marcajes que guarda el reloj.</summary>
    public static string All(string? deviceType) =>
        IsAccess(deviceType) ? "DATA QUERY tablename=Transaction,fields=*" : Range(deviceType, FullStart, FullEnd);

    /// <summary>Marcajes entre <paramref name="fromCivil"/> y <paramref name="toCivil"/>.</summary>
    public static string Range(string? deviceType, DateTime fromCivil, DateTime toCivil) =>
        IsAccess(deviceType)
            ? string.Create(CultureInfo.InvariantCulture, $"DATA QUERY tablename=Transaction,starttime={fromCivil:yyyy-MM-dd HH:mm:ss},endtime={toCivil:yyyy-MM-dd HH:mm:ss}")
            : string.Create(CultureInfo.InvariantCulture, $"DATA QUERY ATTLOG StartTime={fromCivil:yyyy-MM-dd HH:mm:ss}\tEndTime={toCivil:yyyy-MM-dd HH:mm:ss}");
}
