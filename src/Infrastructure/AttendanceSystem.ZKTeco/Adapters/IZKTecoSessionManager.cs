using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.ZKTeco.Adapters;

/// <summary>
/// Gestiona sesiones de conexión bajo demanda a dispositivos ZKTeco,
/// garantizando concurrencia aislada entre múltiples relojes y exclusión mutua por IP.
/// </summary>
public interface IZKTecoSessionManager : IAsyncDisposable
{
    /// <summary>
    /// Conecta a un dispositivo ZKTeco y asocia la conexión a la sesión indicada.
    /// </summary>
    Task<IDeviceClient> ConnectSessionAsync(
        string sessionId,
        string ipAddress,
        int port,
        int timeoutSeconds = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el cliente activo para la sesión especificada.
    /// </summary>
    Task<IDeviceClient> GetSessionAsync(string sessionId);

    /// <summary>
    /// Desconecta y libera los recursos del dispositivo para la sesión especificada.
    /// </summary>
    Task<bool> DisconnectSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cierra y libera todas las sesiones activas (usado en apagado ordenado).
    /// </summary>
    Task CloseAllSessionsAsync(CancellationToken cancellationToken = default);
}
