using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class AttendanceConsolidationRepository : IAttendanceConsolidationRepository
{
    private readonly AttendanceDbContext _context;

    public AttendanceConsolidationRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<AttendanceConsolidationRequest?> GetPendingByEmployeeAsync(
        EmployeeId employeeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceConsolidationRequests
            .Where(x => x.EmployeeId == employeeId &&
                       (x.Status == ConsolidationStatus.Pending || x.Status == ConsolidationStatus.Processing))
            .OrderByDescending(x => x.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceConsolidationRequest>> GetPendingBatchAsync(
        int maxBatchSize,
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceConsolidationRequests
            .Where(x => x.Status == ConsolidationStatus.Pending)
            .OrderBy(x => x.RequestedAt)
            .Take(maxBatchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(AttendanceConsolidationRequest request, CancellationToken cancellationToken = default)
    {
        await _context.AttendanceConsolidationRequests.AddAsync(request, cancellationToken);
    }

    public void Update(AttendanceConsolidationRequest request)
    {
        _context.AttendanceConsolidationRequests.Update(request);
    }
}
