using AttendanceSystem.Application.Features.Shifts.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Queries;

public class ShiftQueries : IShiftQueries
{
     private readonly AttendanceDbContext _dbContext;

    public ShiftQueries(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<ShiftDto>> GetAllShiftsAsync(CancellationToken cancellationToken = default)
    {
        var shifts = await _dbContext.Set<Shift>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return shifts.Select(s => new ShiftDto(
                s.Id.Value,
                s.Name,
                s.StartTime,
                s.EndTime,
                s.ToleranceMinutes,
                s.WorkHours,
                s.ShiftType));
    }
}
