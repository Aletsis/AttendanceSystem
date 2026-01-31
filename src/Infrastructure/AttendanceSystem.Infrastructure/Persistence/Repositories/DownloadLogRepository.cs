using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Repositories;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class DownloadLogRepository : IDownloadLogRepository
{
    private readonly AttendanceDbContext _context;

    public DownloadLogRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<DownloadLog?> GetByIdAsync(
        DownloadLogId id,
        CancellationToken cancellationToken = default)
    {
        return await _context.DownloadLogs
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<DownloadLog>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.DownloadLogs
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DownloadLog>> GetByDeviceIdAsync(
        DeviceId deviceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.DownloadLogs
            .Where(x => x.DeviceId == deviceId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DownloadLog>> GetByDateRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return await _context.DownloadLogs
            .Where(x => x.StartedAt >= from && x.StartedAt <= to)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DownloadLog>> GetRecentAsync(
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        return await _context.DownloadLogs
            .OrderByDescending(x => x.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        DownloadLog downloadLog,
        CancellationToken cancellationToken = default)
    {
        await _context.DownloadLogs.AddAsync(downloadLog, cancellationToken);
    }

    public Task UpdateAsync(
        DownloadLog downloadLog,
        CancellationToken cancellationToken = default)
    {
        _context.DownloadLogs.Update(downloadLog);
        return Task.CompletedTask;
    }
}
