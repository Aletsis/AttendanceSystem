namespace AttendanceSystem.Application.Abstractions;

using AttendanceSystem.Application.DTOs;

/// <summary>
/// Puerto: La aplicación define QUÉ necesita, no CÓMO se implementa.
/// </summary>
public interface IDeviceClient
{
    Task<bool> ConnectAsync(string ipAddress, int port, string? username = null, string? password = null, CancellationToken cancellationToken = default);

    Task<DeviceLogsResult> GetAttendanceLogsAsync(
        string deviceId,
        DateTime? fromDate,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<bool> ClearLogsAsync(
        string deviceId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
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

    /// <summary>Escribe en el reloj la hora indicada, que es hora civil de la zona del reloj.</summary>
    Task<bool> SetDeviceTimeAsync(
        DateTime dateTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajusta el reloj a <paramref name="civilTime"/> (hora civil de su zona) y, si el protocolo lo admite, también su
    /// desfase respecto a UTC. Los relojes por SDK sólo guardan una hora local: el desfase se ignora.
    /// </summary>
    Task<bool> SetDeviceClockAsync(DateTime civilTime, TimeSpan utcOffset, CancellationToken cancellationToken = default)
        => SetDeviceTimeAsync(civilTime, cancellationToken);

    /// <summary>Hora que marca el reloj ahora (civil, de su zona), o null si el protocolo no permite leerla.</summary>
    Task<DateTime?> GetDeviceTimeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<DateTime?>(null);

    Task<bool> SetUserAsync(
        DeviceUserDto user,
        CancellationToken cancellationToken = default);

    Task<DeviceUserDto?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
