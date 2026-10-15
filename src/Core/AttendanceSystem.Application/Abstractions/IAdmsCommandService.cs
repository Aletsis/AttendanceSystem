using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Abstractions;

public interface IAdmsCommandService
{
    void EnqueueCommand(string serialNumber, string command, Guid? downloadLogId = null);
    (string? Command, Guid? DownloadLogId) GetNextCommand(string serialNumber);
    bool HasPendingCommands(string serialNumber);

    // Methods for tracking pending executions
    void RegisterPendingExecution(string serialNumber, string commandId, Guid downloadLogId);
    Guid? GetAndRemovePendingExecution(string commandId);

    // Methods for device info querying and notification
    Task<DeviceInfoDto?> WaitForDeviceInfoAsync(string serialNumber, TimeSpan timeout, CancellationToken cancellationToken = default);
    void NotifyDeviceInfoReceived(string serialNumber, DeviceInfoDto deviceInfo);
}
