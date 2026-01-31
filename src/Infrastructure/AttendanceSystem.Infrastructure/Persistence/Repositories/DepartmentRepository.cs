using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class DepartmentRepository : IDepartmentRepository
{
    private readonly AttendanceDbContext _dbContext;

    public DepartmentRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Department?> GetByIdAsync(DepartmentId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Department>()
            .Include(d => d.Positions)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task AddAsync(Department department, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<Department>().AddAsync(department, cancellationToken);
    }

    public void Update(Department department)
    {
        _dbContext.Set<Department>().Update(department);
    }

    public void Delete(Department department)
    {
        _dbContext.Set<Department>().Remove(department);
    }

    public async Task<IEnumerable<Department>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Department>().ToListAsync(cancellationToken);
    }
}
