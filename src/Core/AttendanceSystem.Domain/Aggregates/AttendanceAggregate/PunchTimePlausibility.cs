namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

/// <summary>
/// Hora imposible de un marcaje: el reloj perdió su fecha (antes del año 2000) o marca una hora posterior a la actual
/// de su zona. Esos marcajes no se ingresan: quedan en cuarentena hasta que alguien los corrija o descarte.
/// No se usa la fecha de alta del reloj como límite: un reloj que ya operaba antes de registrarse trae historial legítimo.
/// </summary>
public static class PunchTimePlausibility
{
    public static readonly DateTime EarliestValid = new(2000, 1, 1);

    /// <summary>Tolerancia hacia el futuro (relojes con unos minutos de adelanto no son imposibles).</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Motivo por el que la hora civil es imposible, o null si es plausible.</summary>
    public static string? Problem(DateTime civilTime, DateTime clockNowCivil)
    {
        if (civilTime < EarliestValid)
            return $"Fecha {civilTime:dd/MM/yyyy HH:mm} anterior al año 2000: el reloj perdió su fecha.";
        if (civilTime - clockNowCivil > FutureTolerance)
            return $"Hora {civilTime:dd/MM/yyyy HH:mm} posterior a la actual del reloj ({clockNowCivil:dd/MM/yyyy HH:mm}): el reloj está adelantado.";
        return null;
    }
}
