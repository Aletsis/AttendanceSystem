using AttendanceSystem.Application.Features.Departments.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

public class DepartmentQueries : IDepartmentQueries
{
     private readonly AttendanceDbContext _dbContext;

    public DepartmentQueries(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<DepartmentDto>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var departments = await _dbContext.Set<Department>()
            .Include(d => d.Positions)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return departments.Select(d => new DepartmentDto(
                d.Id.Value,
                d.Name,
                d.Description,
                d.Positions.Select(p => p.Id.Value).ToList()));
    }

    public async Task<DepartmentDto?> GetDepartmentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await _dbContext.Set<Department>()
            .Include(d => d.Positions)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == Domain.ValueObjects.DepartmentId.From(id), cancellationToken);

        if (department == null)
            return null;

        return new DepartmentDto(
            department.Id.Value,
            department.Name,
            department.Description,
            department.Positions.Select(p => p.Id.Value).ToList());
    }
}
