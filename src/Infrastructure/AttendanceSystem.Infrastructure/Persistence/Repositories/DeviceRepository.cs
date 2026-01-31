using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class DeviceRepository : IDeviceRepository
{
    private readonly AttendanceDbContext _context;

    public DeviceRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<Device?> GetByIdAsync(
        DeviceId id,
        CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> GetActiveDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> GetAllDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .ToListAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastDownloadTimeAsync(
        DeviceId deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await GetByIdAsync(deviceId, cancellationToken);
        return device?.LastDownloadAt;
    }

    public async Task AddAsync(
        Device device,
        CancellationToken cancellationToken = default)
    {
        await _context.Devices.AddAsync(device, cancellationToken);
    }

    public Task UpdateAsync(
        Device device,
        CancellationToken cancellationToken = default)
    {
        _context.Devices.Update(device);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        Device device,
        CancellationToken cancellationToken = default)
    {
        _context.Devices.Remove(device);
        return Task.CompletedTask;
    }

    public async Task ReloadAsync(
        Device device,
        CancellationToken cancellationToken = default)
    {
        await _context.Entry(device).ReloadAsync(cancellationToken);
    }

    public void Detach(Device device)
    {
        _context.Entry(device).State = EntityState.Detached;
    }

    public void ClearChangeTracker()
    {
        _context.ChangeTracker.Clear();
    }
}
