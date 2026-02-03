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

    void ClearChangeTracker();
}