namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Ventanas de búsqueda de salida para jornadas flexibles y continuas (F06-01 / C-03), sustituyendo el
/// límite fijo de 18 h:
/// <list type="bullet">
/// <item>La salida se busca en <c>(entrada, entrada + duración + margen de extras]</c>, con tope absoluto de 24 h
/// (C-03): un marcaje posterior a 24 h nunca se infiere como salida.</item>
/// <item>La ventana termina antes de que empiece la ventana de entrada de la jornada siguiente cuando ésta es
/// laborable: una salida faltante no consume la entrada del día siguiente. Ese límite nunca recorta la duración
/// programada del turno (una guardia de 24 h diaria conserva su salida aunque coincida con la entrada siguiente).</item>
/// </list>
/// Los márgenes son decisiones de negocio registradas (D-05 en el catálogo de contratos), no supuestos del procesador.
/// </summary>
public static class JornadaWindowPolicy
{
    /// <summary>C-03: duración máxima de una jornada inferida automáticamente.</summary>
    public static readonly TimeSpan MaxJornadaDuration = TimeSpan.FromHours(24);

    /// <summary>D-05: tiempo extra máximo después de la duración del turno para aceptar una salida.</summary>
    public static readonly TimeSpan OvertimeAllowanceAfterShift = TimeSpan.FromHours(4);

    /// <summary>D-05: anticipación máxima con la que se acepta la entrada de la jornada siguiente.</summary>
    public static readonly TimeSpan EarlyArrivalAllowance = TimeSpan.FromHours(2);

    /// <summary>Último instante civil aceptable como salida de una jornada que empezó en <paramref name="checkInCivil"/>.</summary>
    public static DateTime ExitWindowEnd(DateTime checkInCivil, TimeSpan workHours, DateTime? nextJornadaEntryStart)
    {
        var span = workHours > TimeSpan.Zero ? workHours + OvertimeAllowanceAfterShift : MaxJornadaDuration;
        if (span > MaxJornadaDuration) span = MaxJornadaDuration;

        var end = checkInCivil + span;
        if (nextJornadaEntryStart is { } next && next.AddTicks(-1) < end)
        {
            var scheduledEnd = checkInCivil + (workHours > MaxJornadaDuration ? MaxJornadaDuration : workHours);
            end = next.AddTicks(-1) > scheduledEnd ? next.AddTicks(-1) : scheduledEnd;
        }
        return end;
    }

    /// <summary>Inicio civil de la ventana de entrada de una jornada laborable que comienza a <paramref name="scheduledStart"/>.</summary>
    public static DateTime EntryWindowStart(DateTime scheduledStart) => scheduledStart - EarlyArrivalAllowance;
}
