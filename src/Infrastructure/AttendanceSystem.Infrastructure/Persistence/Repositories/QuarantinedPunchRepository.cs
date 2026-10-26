using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public sealed class QuarantinedPunchRepository : IQuarantinedPunchRepository
{
    private readonly AttendanceDbContext _context;

    public QuarantinedPunchRepository(AttendanceDbContext context) => _context = context;

    public Task AddRangeAsync(IEnumerable<QuarantinedPunch> punches, CancellationToken cancellationToken = default)
        => _context.QuarantinedPunches.AddRangeAsync(punches, cancellationToken);

    public Task<QuarantinedPunch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.QuarantinedPunches.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<IReadOnlyList<QuarantinedPunch>> GetAsync(QuarantineStatus? status, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.QuarantinedPunches.AsNoTracking();
        if (status.HasValue) query = query.Where(q => q.Status == status.Value);
        return await query.OrderByDescending(q => q.ReceivedAtUtc).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<(string EmployeeId, DateTime ReportedTime)>> GetExistingAsync(
        DeviceId deviceId, IReadOnlyCollection<(string EmployeeId, DateTime ReportedTime)> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0) return new HashSet<(string, DateTime)>();
        var employees = keys.Select(k => k.EmployeeId).Distinct().ToList();
        var existing = await _context.QuarantinedPunches.AsNoTracking()
            .Where(q => q.DeviceId == deviceId && employees.Contains(q.EmployeeId))
            .Select(q => new { q.EmployeeId, q.ReportedTime })
            .ToListAsync(cancellationToken);
        var wanted = keys.Select(k => (k.EmployeeId, Normalize(k.ReportedTime))).ToHashSet();
        return existing.Select(e => (e.EmployeeId, Normalize(e.ReportedTime))).Where(wanted.Contains).ToHashSet();
    }

    private static DateTime Normalize(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
}
