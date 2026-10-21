namespace AttendanceSystem.Domain.Services;

/// <summary>
/// F07-02 / A05: límites de los rangos de fechas antes de consultar o modificar datos. Rechaza rangos invertidos,
/// fechas por defecto o extremas y rangos mayores que el máximo del uso (recálculo, consulta).
/// </summary>
public static class DateRangePolicy
{
    /// <summary>Fecha más antigua admitida (anterior a cualquier dato del sistema).</summary>
    public static readonly DateTime EarliestSupported = new(2000, 1, 1);

    /// <summary>Fecha más lejana admitida: un año hacia el futuro (planeación de horarios).</summary>
    public static DateTime LatestSupported => DateTime.Today.AddYears(1);

    /// <summary>Máximo de días por recálculo (un año); los rangos mayores se procesan por tramos.</summary>
    public const int MaxRecalculationDays = 366;

    /// <summary>Máximo de días por consulta o reporte (un año).</summary>
    public const int MaxQueryDays = 366;

    /// <summary>Mensaje del primer problema del rango, o null si es válido.</summary>
    public static string? Validate(DateTime startDate, DateTime endDate, int maxDays)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (start < EarliestSupported || end < EarliestSupported)
            return $"Las fechas deben ser posteriores al {EarliestSupported:dd/MM/yyyy}.";
        if (start > LatestSupported || end > LatestSupported)
            return $"Las fechas no pueden ser posteriores al {LatestSupported:dd/MM/yyyy}.";
        if (end < start)
            return $"La fecha final ({end:dd/MM/yyyy}) es anterior a la inicial ({start:dd/MM/yyyy}).";
        var days = (end - start).Days + 1;
        if (days > maxDays)
            return $"El rango de {days} días excede el máximo de {maxDays} días.";
        return null;
    }

    /// <summary>Divide un rango en tramos consecutivos de como máximo <paramref name="maxDays"/> días.</summary>
    public static IEnumerable<(DateTime Start, DateTime End)> Slices(DateTime startDate, DateTime endDate, int maxDays)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        while (start <= end)
        {
            var sliceEnd = start.AddDays(maxDays - 1);
            if (sliceEnd > end) sliceEnd = end;
            yield return (start, sliceEnd);
            start = sliceEnd.AddDays(1);
        }
    }
}
