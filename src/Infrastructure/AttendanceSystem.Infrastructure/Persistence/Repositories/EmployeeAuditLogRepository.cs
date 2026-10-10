namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

public class EmployeeAuditLogRepository : IEmployeeAuditLogRepository
{
    private readonly AttendanceDbContext _context;

    public EmployeeAuditLogRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeeAuditLog>> GetByEmployeeIdAsync(
        EmployeeId employeeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.EmployeeAuditLogs
            .AsNoTracking()
            .Where(l => l.EmployeeId == employeeId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        EmployeeAuditLog log,
        CancellationToken cancellationToken = default)
    {
        await _context.EmployeeAuditLogs.AddAsync(log, cancellationToken);
    }
}
