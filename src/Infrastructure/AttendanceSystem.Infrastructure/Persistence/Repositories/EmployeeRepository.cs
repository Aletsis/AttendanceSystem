using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly AttendanceDbContext _dbContext;

    public EmployeeRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .ToListAsync(cancellationToken);
    }

    public async Task<Employee?> GetByIdAsync(EmployeeId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Employee>> GetByBranchAsync(BranchId branchId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .Where(e => e.BranchId == branchId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Employee>> GetByDepartmentAsync(DepartmentId departmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .Where(e => e.DepartmentId == departmentId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(EmployeeId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.Id == id, cancellationToken);
    }

    public void Add(Employee employee)
    {
        _dbContext.Set<Employee>().Add(employee);
    }

    public void Update(Employee employee)
    {
        _dbContext.Set<Employee>().Update(employee);
    }

    public void Delete(Employee employee)
    {
        _dbContext.Set<Employee>().Remove(employee);
    }

    public async Task<bool> IsShiftInUseAsync(ShiftId shiftId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.ScheduleId == shiftId, cancellationToken);
    }

    public async Task<bool> IsDepartmentInUseAsync(DepartmentId departmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.DepartmentId == departmentId, cancellationToken);
    }

    public async Task<bool> IsPositionInUseAsync(PositionId positionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.PositionId == positionId, cancellationToken);
    }
}
