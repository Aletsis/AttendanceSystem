using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Abstractions;

public interface IAdmsCommandService
{
    void EnqueueCommand(string serialNumber, string command, Guid? downloadLogId = null);
    (string? CommandId, string? Command, Guid? DownloadLogId) GetNextCommand(string serialNumber);
    bool HasPendingCommands(string serialNumber);

    // Correlación de ejecución segura por SerialNumber + CommandID
    Task<Guid?> AcknowledgeCommandExecutionAsync(string serialNumber, string commandId, int returnCode, string? errorMessage = null, CancellationToken cancellationToken = default);
    Guid? GetAndRemovePendingExecution(string serialNumber, string commandId);
    Guid? GetAndRemovePendingExecution(string commandId);
    void RegisterPendingExecution(string serialNumber, string commandId, Guid downloadLogId);

    // Métodos para Device Info y sincronización en tiempo real
    Task<DeviceInfoDto?> WaitForDeviceInfoAsync(string serialNumber, TimeSpan timeout, CancellationToken cancellationToken = default);
    void NotifyDeviceInfoReceived(string serialNumber, DeviceInfoDto deviceInfo, string? commandId = null);

    // Rastreo de sesiones y métricas de descarga ADMS
    Guid? GetActiveDownloadLogId(string serialNumber, string? commandId = null);
    void RecordDownloadProgress(string serialNumber, string? commandId, int totalReceived, int newInserted);
    (int TotalReceived, int NewInserted) GetAndCompleteDownloadSession(string serialNumber, string commandId);
    void NotifyDownloadCompleted(string serialNumber, Guid downloadLogId, bool isSuccess, int totalDownloaded, int newRecords, string? errorMessage = null);
    event Action<string, Guid, bool, int, int, string?>? OnDownloadStatusChanged;
}
