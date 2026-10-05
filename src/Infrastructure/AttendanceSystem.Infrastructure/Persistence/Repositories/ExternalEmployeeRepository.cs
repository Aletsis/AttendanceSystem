using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class ExternalEmployeeRepository : IExternalEmployeeRepository
{
    private readonly AttendanceDbContext _dbContext;

    public ExternalEmployeeRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ExternalEmployee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalEmployees
            .OrderBy(e => e.FirstName)
            .ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExternalEmployee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalEmployees
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalEmployee>> GetByBranchAsync(BranchId branchId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalEmployees
            .Where(e => e.BranchId == branchId)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExternalEmployee?> GetByBranchAndEmployeeNumberAsync(BranchId branchId, string employeeNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalEmployees
            .FirstOrDefaultAsync(e => e.BranchId == branchId && e.EmployeeNumber == employeeNumber, cancellationToken);
    }

    public async Task<bool> ExistsInBranchAsync(BranchId branchId, string employeeNumber, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ExternalEmployees
            .Where(e => e.BranchId == branchId && e.EmployeeNumber == employeeNumber);

        if (excludeId.HasValue)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public void Add(ExternalEmployee externalEmployee)
    {
        _dbContext.ExternalEmployees.Add(externalEmployee);
    }

    public void Update(ExternalEmployee externalEmployee)
    {
        _dbContext.ExternalEmployees.Update(externalEmployee);
    }

    public void Delete(ExternalEmployee externalEmployee)
    {
        _dbContext.ExternalEmployees.Remove(externalEmployee);
    }
}
