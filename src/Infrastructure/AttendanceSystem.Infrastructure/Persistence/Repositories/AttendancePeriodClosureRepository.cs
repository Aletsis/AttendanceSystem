using AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;
using AttendanceSystem.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public sealed class AttendancePeriodClosureRepository : IAttendancePeriodClosureRepository
{
    private readonly AttendanceDbContext _context;

    public AttendancePeriodClosureRepository(AttendanceDbContext context) => _context = context;

    public async Task<IReadOnlyList<AttendancePeriodClosure>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.AttendancePeriodClosures.OrderBy(c => c.StartDate).ToListAsync(cancellationToken);

    public Task AddAsync(AttendancePeriodClosure closure, CancellationToken cancellationToken = default) =>
        _context.AttendancePeriodClosures.AddAsync(closure, cancellationToken).AsTask();

    public void Update(AttendancePeriodClosure closure) => _context.AttendancePeriodClosures.Update(closure);
}
