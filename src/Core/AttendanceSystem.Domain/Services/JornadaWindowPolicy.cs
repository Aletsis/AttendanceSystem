namespace AttendanceSystem.Domain.Services;

/// <summary>
/// Ventanas de búsqueda de salida para jornadas flexibles y continuas (F06-01), sustituyendo el límite fijo
/// de 18 h. Reglas acordadas (D-05):
/// <list type="bullet">
/// <item>Un marcaje del mismo día laboral siempre puede ser la salida; la ventana sólo limita la búsqueda en el
/// día siguiente.</item>
/// <item>La ventana se ancla en la entrada programada, o en la real si llegó tarde: llegar antes no la acorta.</item>
/// <item>Fin = ancla + duración del turno + 4 h. Sin duración definida, ancla + 24 h (C-03).</item>
/// <item>La ventana termina antes de la ventana de entrada de la jornada siguiente cuando ésta es laborable, sin
/// recortar la duración programada del turno. Sin duración definida (bolsa semanal) no hay duración que respetar y
/// la jornada siguiente siempre la corta.</item>
/// </list>
/// </summary>
public static class JornadaWindowPolicy
{
    /// <summary>C-03: duración máxima de una jornada sin duración definida.</summary>
    public static readonly TimeSpan MaxJornadaDuration = TimeSpan.FromHours(24);

    /// <summary>D-05: tiempo extra máximo después de la duración del turno para aceptar una salida.</summary>
    public static readonly TimeSpan OvertimeAllowanceAfterShift = TimeSpan.FromHours(4);

    /// <summary>D-05: anticipación máxima con la que se acepta la entrada de la jornada siguiente.</summary>
    public static readonly TimeSpan EarlyArrivalAllowance = TimeSpan.FromHours(2);

    /// <summary>
    /// Último instante civil aceptable como salida en el día siguiente de una jornada con entrada real
    /// <paramref name="checkInCivil"/> y entrada programada <paramref name="scheduledStartCivil"/> (null si el
    /// turno no tiene ventana de llegada).
    /// </summary>
    public static DateTime ExitWindowEnd(DateTime checkInCivil, DateTime? scheduledStartCivil, TimeSpan workHours, DateTime? nextJornadaEntryStart)
    {
        var anchor = scheduledStartCivil is { } scheduled && scheduled > checkInCivil ? scheduled : checkInCivil;
        var end = workHours > TimeSpan.Zero ? anchor + workHours + OvertimeAllowanceAfterShift : anchor + MaxJornadaDuration;

        if (nextJornadaEntryStart is { } next && next.AddTicks(-1) < end)
        {
            if (workHours <= TimeSpan.Zero) return next.AddTicks(-1);
            var scheduledEnd = anchor + workHours;
            end = next.AddTicks(-1) > scheduledEnd ? next.AddTicks(-1) : scheduledEnd;
        }
        return end;
    }

    /// <summary>Entrada programada civil de una jornada, o null si el turno no tiene ventana de llegada.</summary>
    public static DateTime? ScheduledStart(DateTime date, TimeSpan startTime, bool hasEntryWindow) =>
        hasEntryWindow ? date.Date.Add(startTime) : null;

    /// <summary>Inicio civil de la ventana de entrada de una jornada laborable que comienza a <paramref name="scheduledStart"/>.</summary>
    public static DateTime EntryWindowStart(DateTime scheduledStart) => scheduledStart - EarlyArrivalAllowance;
}
