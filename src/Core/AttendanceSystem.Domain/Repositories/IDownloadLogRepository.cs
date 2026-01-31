using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;

namespace AttendanceSystem.Domain.Repositories;

public interface IDownloadLogRepository
{
    Task<DownloadLog?> GetByIdAsync(DownloadLogId id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DownloadLog>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<DownloadLog>> GetByDeviceIdAsync(DeviceId deviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DownloadLog>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IEnumerable<DownloadLog>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default);
    Task AddAsync(DownloadLog downloadLog, CancellationToken cancellationToken = default);
    Task UpdateAsync(DownloadLog downloadLog, CancellationToken cancellationToken = default);
}
