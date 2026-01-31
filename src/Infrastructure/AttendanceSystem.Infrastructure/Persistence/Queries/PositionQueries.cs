using AttendanceSystem.Application.Features.Positions.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

public class PositionQueries : IPositionQueries
{
     private readonly AttendanceDbContext _dbContext;

    public PositionQueries(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<PositionDto>> GetAllPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _dbContext.Set<Position>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return positions.Select(p => new PositionDto(
                p.Id.Value,
                p.Name,
                p.Description,
                p.BaseSalary));
    }
}
