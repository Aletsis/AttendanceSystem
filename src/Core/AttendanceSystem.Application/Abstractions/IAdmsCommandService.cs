using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Cola de comandos para relojes ADMS. Vive en la tabla AdmsCommands, no en memoria: quien encola (la web, cualquiera de
/// sus workers o el cliente WPF) y quien atiende al reloj cuando pide comandos pueden ser procesos distintos.
/// </summary>
public interface IAdmsCommandService
{
    /// <summary>Guarda el comando para el reloj; devuelve su identificador para esperar la respuesta.</summary>
    Task<Guid> EnqueueCommandAsync(string serialNumber, string command, Guid? downloadLogId = null, CancellationToken cancellationToken = default);
    /// <summary>Reclama el siguiente comando pendiente del reloj (una sola vez entre todos los procesos).</summary>
    Task<(string? CommandId, string? Command, Guid? DownloadLogId)> GetNextCommandAsync(string serialNumber, CancellationToken cancellationToken = default);
    Task<bool> HasPendingCommandsAsync(string serialNumber, CancellationToken cancellationToken = default);

    // Correlación de ejecución segura por SerialNumber + CommandID
    Task<Guid?> AcknowledgeCommandExecutionAsync(string serialNumber, string commandId, int returnCode, string? errorMessage = null, CancellationToken cancellationToken = default);
    Guid? GetAndRemovePendingExecution(string serialNumber, string commandId);
    Guid? GetAndRemovePendingExecution(string commandId);
    void RegisterPendingExecution(string serialNumber, string commandId, Guid downloadLogId);

    // Métodos para Device Info y sincronización en tiempo real
    /// <summary>Espera la respuesta del reloj al comando INFO <paramref name="infoCommandId"/>; null si no llega a tiempo.</summary>
    Task<DeviceInfoDto?> WaitForDeviceInfoAsync(Guid infoCommandId, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task NotifyDeviceInfoReceivedAsync(string serialNumber, DeviceInfoDto deviceInfo, string? commandId = null, CancellationToken cancellationToken = default);

    // Rastreo de sesiones y métricas de descarga ADMS (en el proceso que atiende al reloj)
    Guid? GetActiveDownloadLogId(string serialNumber, string? commandId = null);
    void RecordDownloadProgress(string serialNumber, string? commandId, int totalReceived, int newInserted);
    (int TotalReceived, int NewInserted) GetAndCompleteDownloadSession(string serialNumber, string commandId);
    void NotifyDownloadCompleted(string serialNumber, Guid downloadLogId, bool isSuccess, int totalDownloaded, int newRecords, string? errorMessage = null);
    event Action<string, Guid, bool, int, int, string?>? OnDownloadStatusChanged;
}
