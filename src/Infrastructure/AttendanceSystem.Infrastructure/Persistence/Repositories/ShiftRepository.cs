using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class ShiftRepository : IShiftRepository
{
    private readonly AttendanceDbContext _dbContext;

    public ShiftRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Shift?> GetByIdAsync(ShiftId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Shift>()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task AddAsync(Shift shift, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<Shift>().AddAsync(shift, cancellationToken);
    }

    public void Update(Shift shift)
    {
        _dbContext.Set<Shift>().Update(shift);
    }

    public void Delete(Shift shift)
    {
        _dbContext.Set<Shift>().Remove(shift);
    }

    public async Task<IEnumerable<Shift>> GetAllAsync(CancellationToken cancellationToken = default)
    {
         return await _dbContext.Set<Shift>().ToListAsync(cancellationToken);
    }
}
