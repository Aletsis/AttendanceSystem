namespace AttendanceSystem.Application.Abstractions;

using AttendanceSystem.Application.DTOs;

// Puerto: La aplicación define QUÉ necesita, no CÓMO se implementa
public interface IZKTecoDeviceClient
{
    Task<bool> ConnectAsync(
        string ipAddress, 
        int port, 
        CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<RawAttendanceRecord>> GetAttendanceLogsAsync(
        string deviceId,
        DateTime? fromDate,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    
    Task<bool> ClearLogsAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
    
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    
    Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(
        CancellationToken cancellationToken = default);

    Task<bool> DeleteUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteUserFingerprintsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> ResetToFactorySettingsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> SetDeviceTimeAsync(
        DateTime dateTime,
        CancellationToken cancellationToken = default);

    Task<bool> SetUserAsync(
        DeviceUserDto user,
        CancellationToken cancellationToken = default);
}

