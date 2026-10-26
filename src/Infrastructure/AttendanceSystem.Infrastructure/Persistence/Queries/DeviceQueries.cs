using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

public class DeviceQueries : IDeviceQueries
{
    private readonly IDbContextFactory<AttendanceDbContext> _contextFactory;
    private readonly IDeviceHeartbeatTracker _heartbeatTracker;

    public DeviceQueries(
        IDbContextFactory<AttendanceDbContext> contextFactory,
        IDeviceHeartbeatTracker heartbeatTracker)
    {
        _contextFactory = contextFactory;
        _heartbeatTracker = heartbeatTracker;
    }

    public async Task<IEnumerable<DeviceDto>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Usamos AsNoTracking para mejor rendimiento en lecturas
        var devices = await context.Devices
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return devices.Select(d =>
        {
            var lastActivity = _heartbeatTracker.GetLastActivity(d.HardwareInfo?.SerialNumber, d.Id.Value);
            var statusName = ResolveStatus(d);

            return new DeviceDto(
                d.Id.Value.ToString(),
                d.Name,
                d.IpAddress,
                d.Port,
                d.Location,
                d.IsActive,
                statusName,
                d.Brand,
                d.DownloadMethod,
                d.LastDownloadAt,
                d.TotalDownloadCount,
                d.ShouldClearAfterDownload,
                d.HardwareInfo?.SerialNumber,
                d.HardwareInfo?.FirmwareVersion,
                d.HardwareInfo?.Platform,
                d.HardwareInfo?.UserCount,
                d.HardwareInfo?.FingerprintCount,
                d.HardwareInfo?.FaceCount,
                d.HardwareInfo?.AttendanceRecordCount,
                d.HardwareInfo?.UserCapacity,
                d.HardwareInfo?.FingerprintCapacity,
                d.HardwareInfo?.FaceCapacity,
                d.HardwareInfo?.AttendanceRecordCapacity,
                d.Username,
                d.Password,
                d.DeviceType,
                d.HardwareInfo?.PushVersion,
                d.HardwareInfo?.SdkVersion,
                d.HardwareInfo?.MacAddress,
                d.TimeZoneId,
                lastActivity,
                d.AcceptPunchesFrom,
                d.CreatedAt
            );
        });
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

        var lastActivity = _heartbeatTracker.GetLastActivity(device.HardwareInfo?.SerialNumber, device.Id.Value);
        var statusName = ResolveStatus(device);

        return new DeviceDto(
            device.Id.Value.ToString(),
            device.Name,
            device.IpAddress,
            device.Port,
            device.Location,
            device.IsActive,
            statusName,
            device.Brand,
            device.DownloadMethod,
            device.LastDownloadAt,
            device.TotalDownloadCount,
            device.ShouldClearAfterDownload,
            device.HardwareInfo?.SerialNumber,
            device.HardwareInfo?.FirmwareVersion,
            device.HardwareInfo?.Platform,
            device.HardwareInfo?.UserCount,
            device.HardwareInfo?.FingerprintCount,
            device.HardwareInfo?.FaceCount,
            device.HardwareInfo?.AttendanceRecordCount,
            device.HardwareInfo?.UserCapacity,
            device.HardwareInfo?.FingerprintCapacity,
            device.HardwareInfo?.FaceCapacity,
            device.HardwareInfo?.AttendanceRecordCapacity,
            device.Username,
            device.Password,
            device.DeviceType,
            device.HardwareInfo?.PushVersion,
            device.HardwareInfo?.SdkVersion,
            device.HardwareInfo?.MacAddress,
            device.TimeZoneId,
            lastActivity,
            device.AcceptPunchesFrom,
            device.CreatedAt
        );
    }

    private string ResolveStatus(Device device)
    {
        if (device.DownloadMethod == DeviceDownloadMethod.Adms)
        {
            var isOnline = _heartbeatTracker.IsDeviceOnline(device.HardwareInfo?.SerialNumber, device.Id.Value);
            if (isOnline)
            {
                return DeviceStatus.Online.Name;
            }

            // Si es ADMS y no hay latidos recientes, si la BD decía Error lo preservamos, de lo contrario Desconectado
            if (device.Status == DeviceStatus.Error)
            {
                return DeviceStatus.Error.Name;
            }

            return DeviceStatus.Offline.Name;
        }

        return device.Status.Name;
    }
}
