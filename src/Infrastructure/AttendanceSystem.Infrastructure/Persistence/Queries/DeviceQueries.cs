using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

public class DeviceQueries : IDeviceQueries
{
    private readonly IDbContextFactory<AttendanceDbContext> _contextFactory;

    public DeviceQueries(IDbContextFactory<AttendanceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<DeviceDto>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        // Usamos AsNoTracking para mejor rendimiento en lecturas
        var devices = await context.Devices
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return devices.Select(d => new DeviceDto(
            d.Id.Value.ToString(),
            d.Name,
            d.IpAddress,
            d.Port,
            d.Location,
            d.IsActive,
            d.Status.Name,
            d.DownloadMethod,
            d.LastDownloadAt,
            d.TotalDownloadCount,
            d.ShouldClearAfterDownload,
            d.HardwareInfo.SerialNumber,
            d.HardwareInfo.FirmwareVersion,
            d.HardwareInfo.Platform,
            d.HardwareInfo.UserCount,
            d.HardwareInfo.FingerprintCount,
            d.HardwareInfo.FaceCount,
            d.HardwareInfo.AttendanceRecordCount,
            d.HardwareInfo.UserCapacity,
            d.HardwareInfo.FingerprintCapacity,
            d.HardwareInfo.FaceCapacity,
            d.HardwareInfo.AttendanceRecordCapacity
        ));

    }

    public async Task<DeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;

        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        var id = DeviceId.From(deviceId);
        var device = await context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            
        if (device == null) return null;

        return new DeviceDto(
            device.Id.Value.ToString(),
            device.Name,
            device.IpAddress,
            device.Port,
            device.Location,
            device.IsActive,
            device.Status.Name,
            device.DownloadMethod,
            device.LastDownloadAt,
            device.TotalDownloadCount,
            device.ShouldClearAfterDownload,
            device.HardwareInfo.SerialNumber,
            device.HardwareInfo.FirmwareVersion,
            device.HardwareInfo.Platform,
            device.HardwareInfo.UserCount,
            device.HardwareInfo.FingerprintCount,
            device.HardwareInfo.FaceCount,
            device.HardwareInfo.AttendanceRecordCount,
            device.HardwareInfo.UserCapacity,
            device.HardwareInfo.FingerprintCapacity,
            device.HardwareInfo.FaceCapacity,
            device.HardwareInfo.AttendanceRecordCapacity
        );
    }
}
