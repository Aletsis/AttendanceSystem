using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Abstractions;

public interface IDeviceDiscoveryService
{
    Task<IReadOnlyList<DiscoveredDeviceDto>> DiscoverDevicesAsync(CancellationToken cancellationToken = default);
}
