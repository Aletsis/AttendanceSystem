using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class RotationPatternRepository : IRotationPatternRepository
{
    private readonly AttendanceDbContext _context;

    public RotationPatternRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<RotationPattern?> GetByEmployeeAsync(EmployeeId employeeId, CancellationToken cancellationToken = default)
    {
        return await _context.RotationPatterns.FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken);
    }

    public async Task<IReadOnlyList<RotationPattern>> GetIndefiniteAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RotationPatterns.Where(p => p.EndDate == null).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RotationPattern pattern, CancellationToken cancellationToken = default)
    {
        await _context.RotationPatterns.AddAsync(pattern, cancellationToken);
    }
}
