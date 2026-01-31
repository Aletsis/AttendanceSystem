using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class PositionRepository : IPositionRepository
{
    private readonly AttendanceDbContext _dbContext;

    public PositionRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Position?> GetByIdAsync(PositionId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Position>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(Position position, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<Position>().AddAsync(position, cancellationToken);
    }

    public void Update(Position position)
    {
        _dbContext.Set<Position>().Update(position);
    }

    public void Delete(Position position)
    {
        _dbContext.Set<Position>().Remove(position);
    }

    public async Task<IEnumerable<Position>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Position>().ToListAsync(cancellationToken);
    }
}
