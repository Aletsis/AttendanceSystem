using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class ExternalAttendanceLogRepository : IExternalAttendanceLogRepository
{
    private readonly AttendanceDbContext _dbContext;

    public ExternalAttendanceLogRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ExternalAttendanceLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.ExternalAttendanceLogs.AddAsync(log, cancellationToken);
    }

    public Task UpdateAsync(ExternalAttendanceLog log, CancellationToken cancellationToken = default)
    {
        _dbContext.ExternalAttendanceLogs.Update(log);
        return Task.CompletedTask;
    }

    public async Task<ExternalAttendanceLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalAttendanceLogs
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalAttendanceLog>> GetPendingOrFailedLogsAsync(int maxCount = 100, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalAttendanceLogs
            .Where(x => x.Status == ExternalLogStatus.Pending || x.Status == ExternalLogStatus.Failed)
            .OrderBy(x => x.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalAttendanceLog>> GetRecentLogsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalAttendanceLogs
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
