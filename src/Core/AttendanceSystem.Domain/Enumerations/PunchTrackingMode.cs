namespace AttendanceSystem.Domain.Enumerations;

public enum PunchTrackingMode
{
    SingleInterval = 0, // Tiempo corrido (1 entrada, 1 salida)
    MultiInterval = 1   // Multi-marcaje (múltiples pares de entradas y salidas libres acumulables)
}
