namespace AttendanceSystem.Domain.Services;

public class AttendanceValidationService
{
    // Reglas de negocio que no pertenecen a un agregado específico
    public bool IsValidCheckSequence(
        CheckType previousCheck, 
        CheckType currentCheck)
    {
        // Lógica: No puede haber dos entradas seguidas
        if (previousCheck == CheckType.CheckIn && currentCheck == CheckType.CheckIn)
            return false;
        
        // No puede haber dos salidas seguidas
        if (previousCheck == CheckType.CheckOut && currentCheck == CheckType.CheckOut)
            return false;
        
        return true;
    }

    public TimeSpan CalculateWorkedHours(
        DateTime checkIn, 
        DateTime checkOut,
        IEnumerable<(DateTime start, DateTime end)> breaks)
    {
        var totalTime = checkOut - checkIn;
        var breakTime = breaks.Sum(b => (b.end - b.start).TotalMinutes);
        
        return TimeSpan.FromMinutes(totalTime.TotalMinutes - breakTime);
    }

    public bool IsLateArrival(DateTime checkIn, TimeSpan scheduledStartTime)
    {
        var checkInTime = checkIn.TimeOfDay;
        var toleranceMinutes = 15; // Regla de negocio: 15 min de tolerancia
        
        var maxAllowedTime = scheduledStartTime.Add(TimeSpan.FromMinutes(toleranceMinutes));
        
        return checkInTime > maxAllowedTime;
    }
}