using AttendanceSystem.Domain.Aggregates.ShiftAggregate;

namespace AttendanceSystem.Domain.Services;

public static class ShiftDetectionService
{
    /// <summary>
    /// Encuentra el turno más cercano a la hora de marcaje dada,
    /// calculando la distancia circular en minutos dentro de una escala de 24 horas.
    /// </summary>
    /// <param name="punchTime">Hora del marcaje biométrico (típicamente el primer marcaje del día).</param>
    /// <param name="candidateShifts">Colección de turnos candidatos para la detección.</param>
    /// <param name="maxProximityMinutes">Umbral máximo de proximidad permitido en minutos (por defecto 300 min = 5 horas).</param>
    /// <returns>El turno candidato más cercano o null si ninguno califica o la lista está vacía.</returns>
    public static Shift? FindClosestShift(
        DateTime punchTime,
        IEnumerable<Shift> candidateShifts,
        int maxProximityMinutes = 300)
    {
        if (candidateShifts == null) return null;

        var timeOfDay = punchTime.TimeOfDay;
        Shift? bestShift = null;
        double minDifferenceMinutes = double.MaxValue;

        foreach (var shift in candidateShifts)
        {
            var shiftStart = shift.StartTime;

            // Distancia circular en minutos entre timeOfDay y shiftStart (escala 24h)
            var diff = Math.Abs((timeOfDay - shiftStart).TotalMinutes);
            if (diff > 720)
            {
                diff = 1440 - diff;
            }

            if (diff < minDifferenceMinutes)
            {
                minDifferenceMinutes = diff;
                bestShift = shift;
            }
        }

        if (bestShift != null && minDifferenceMinutes <= maxProximityMinutes)
        {
            return bestShift;
        }

        return null;
    }
}
