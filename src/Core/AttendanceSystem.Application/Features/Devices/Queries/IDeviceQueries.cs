using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories; // Reusing Device entity or DTOs
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Queries;

public interface IDeviceQueries
{
    Task<IEnumerable<DeviceDto>> GetAllDevicesAsync(CancellationToken cancellationToken = default);
    Task<DeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default);
}
