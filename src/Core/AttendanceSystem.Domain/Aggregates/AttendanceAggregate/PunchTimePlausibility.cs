using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Services;

namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

/// <summary>
/// Horas imposibles para un marcaje según la política (R1 y R2 del caso 18). La misma validación decide la cuarentena
/// y si una hora corregida puede liberarse.
/// </summary>
public static class PunchTimePlausibility
{
    /// <summary>Piso absoluto: ningún reloj acepta marcajes anteriores, aunque la política permita más.</summary>
    public static readonly DateTime EarliestValid = new(2000, 1, 1);

    /// <summary>Fecha mínima de los marcajes del reloj (R1).</summary>
    public static DateTime MinimumDate(Device? device, PunchQuarantinePolicy policy)
    {
        var minDate = device?.MinimumPunchDate(policy.MinDateDaysBeforeDevice) ?? EarliestValid;
        return minDate < EarliestValid ? EarliestValid : minDate;
    }

    /// <summary>R1: el reloj perdió su fecha.</summary>
    public static string? MinDateProblem(DateTime civilTime, DateTime minDate) =>
        civilTime < minDate
            ? $"Fecha {civilTime:dd/MM/yyyy HH:mm} anterior a la fecha mínima del reloj ({minDate:dd/MM/yyyy}): el reloj perdió su fecha."
            : null;

    /// <summary>R2: el reloj está adelantado.</summary>
    public static string? FutureProblem(DateTime civilTime, DateTime clockNowCivil, TimeSpan futureTolerance) =>
        civilTime - clockNowCivil > futureTolerance
            ? $"Hora {civilTime:dd/MM/yyyy HH:mm} posterior a la actual del reloj ({clockNowCivil:dd/MM/yyyy HH:mm}): el reloj está adelantado."
            : null;

    /// <summary>Motivo por el que la hora civil es imposible, o null si es plausible.</summary>
    public static string? Problem(DateTime civilTime, DateTime clockNowCivil, DateTime minDate, TimeSpan futureTolerance) =>
        MinDateProblem(civilTime, minDate) ?? FutureProblem(civilTime, clockNowCivil, futureTolerance);
}
