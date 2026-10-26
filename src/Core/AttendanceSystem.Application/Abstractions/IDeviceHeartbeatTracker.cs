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

    /// <summary>
    /// Caso 18: desde cuándo (UTC) el reloj se comunica sin interrupción; null si no hay registro. Un marcaje con hora
    /// anterior que llega "en tiempo real" de un reloj conectado desde antes de esa hora es de un reloj atrasado.
    /// Se reinicia al reiniciar el servidor.
    /// </summary>
    DateTime? GetConnectedSince(string? serialNumber, string? deviceId = null) => null;
}
