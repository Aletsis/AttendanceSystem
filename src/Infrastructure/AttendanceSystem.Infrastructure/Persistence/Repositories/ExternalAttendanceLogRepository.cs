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
            // Los menos intentados primero: un log que falla siempre no bloquea a los nuevos.
            .OrderBy(x => x.RetryCount)
            .ThenBy(x => x.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalAttendanceLog>> ExcludeAlreadyStoredAsync(IReadOnlyList<ExternalAttendanceLog> candidates, CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return candidates;

        var employeeIds = candidates.Select(c => c.EmployeeId).Distinct().ToList();
        var from = candidates.Min(c => c.CheckTime);
        var to = candidates.Max(c => c.CheckTime);

        var stored = await _dbContext.ExternalAttendanceLogs
            .AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId) && x.CheckTime >= from && x.CheckTime <= to)
            .Select(x => new { x.BranchCode, x.EmployeeId, x.CheckTime })
            .ToListAsync(cancellationToken);

        var seen = stored.Select(x => (x.BranchCode, x.EmployeeId, x.CheckTime)).ToHashSet();
        return candidates.Where(c => seen.Add((c.BranchCode, c.EmployeeId, c.CheckTime))).ToList();
    }

    public async Task<IReadOnlyList<ExternalAttendanceLog>> GetRecentLogsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalAttendanceLogs
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
