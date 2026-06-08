using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class SystemAlertRepository : ISystemAlertRepository
{
    private readonly AttendanceDbContext _dbContext;

    public SystemAlertRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(SystemAlert alert, CancellationToken cancellationToken = default)
    {
        await _dbContext.SystemAlerts.AddAsync(alert, cancellationToken);
    }

    public async Task<bool> ExistsAsync(AlertType type, string referenceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SystemAlerts
            .AnyAsync(a => a.Type == type && a.ReferenceId == referenceId, cancellationToken);
    }

    public async Task<IEnumerable<SystemAlert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SystemAlerts
            .Where(a => !a.IsResolved)
            .ToListAsync(cancellationToken);
    }
}
