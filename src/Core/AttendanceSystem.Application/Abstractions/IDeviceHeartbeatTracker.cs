namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Servicio en memoria para rastrear la última actividad y presencia de dispositivos biométricos (especialmente ADMS / Push).
/// </summary>
public interface IDeviceHeartbeatTracker
{
    /// <summary>
    /// Registra un latido o comunicación entrante de un dispositivo.
    /// </summary>
    void RecordActivity(string serialNumber, string? deviceId = null);

    /// <summary>
    /// Obtiene la fecha y hora UTC de la última actividad registrada para un dispositivo.
    /// </summary>
    DateTime? GetLastActivity(string? serialNumber, string? deviceId = null);

    /// <summary>
    /// Determina si un dispositivo se encuentra en línea basado en su última actividad.
    /// </summary>
    bool IsDeviceOnline(string? serialNumber, string? deviceId = null, TimeSpan? timeout = null);
}
