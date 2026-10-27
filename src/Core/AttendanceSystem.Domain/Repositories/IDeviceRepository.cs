public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(
        DeviceId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> GetActiveDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> GetAllDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<Device?> GetBySerialNumberAsync(
        string serialNumber,
        CancellationToken cancellationToken = default);

    Task<DateTime?> GetLastAttLogTimestampAsync(
        string serialNumber,
        CancellationToken cancellationToken = default);

    Task ResetAttLogTimestampAsync(
        string serialNumber,
        CancellationToken cancellationToken = default);

    Task UpdateLastAttLogTimestampAsync(
        string serialNumber,
        DateTime lastLogTime,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Device device,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Device device,
        CancellationToken cancellationToken = default);

    Task<DateTime?> GetLastDownloadTimeAsync(
        DeviceId deviceId,
        CancellationToken cancellationToken = default);

    Task ReloadAsync(
        Device device,
        CancellationToken cancellationToken = default);

    void Detach(Device device);

    /// <summary>
    /// Si el reloj tiene historial (checadas, descargas, cuarentena, incidentes o correcciones de hora). Con historial no
    /// se borra: las checadas de un reloj nunca se eliminan y deben seguir apuntando a él.
    /// </summary>
    Task<bool> HasHistoryAsync(
        DeviceId deviceId,
        CancellationToken cancellationToken = default);

    void Delete(Device device);

    void ClearChangeTracker();
}
