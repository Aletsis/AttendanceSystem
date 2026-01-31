using AttendanceSystem.Application.Features.Branches.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

public class BranchQueries : IBranchQueries
{
     private readonly AttendanceDbContext _dbContext;

    public BranchQueries(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<BranchDto>> GetAllBranchesAsync(CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Set<Branch>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return branches.Select(b => new BranchDto(
                b.Id.Value,
                b.Name,
                b.Description,
                b.Address));
    }
}
