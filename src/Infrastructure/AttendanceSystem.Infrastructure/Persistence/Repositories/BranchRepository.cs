using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class BranchRepository : IBranchRepository
{
    private readonly AttendanceDbContext _dbContext;

    public BranchRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Branch?> GetByIdAsync(BranchId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Branch>()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task AddAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<Branch>().AddAsync(branch, cancellationToken);
    }

    public void Update(Branch branch)
    {
        _dbContext.Set<Branch>().Update(branch);
    }

    public void Delete(Branch branch)
    {
        _dbContext.Set<Branch>().Remove(branch);
    }

    public async Task<IEnumerable<Branch>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Branch>().ToListAsync(cancellationToken);
    }
}
