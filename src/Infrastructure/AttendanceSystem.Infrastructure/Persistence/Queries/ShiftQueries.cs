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
            .Include(s => s.Days)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var byId = shifts.ToDictionary(s => s.Id);
        return shifts.Select(s => new ShiftDto(
                s.Id.Value,
                s.Name,
                s.StartTime,
                s.EndTime,
                s.ToleranceMinutes,
                s.WorkHours,
                s.ShiftType,
                s.Days.Select(d => d.DayShiftId is { } dayShiftId && byId.TryGetValue(dayShiftId, out var dayShift)
                    ? new ShiftDayDto(d.DayOfWeek, dayShift.StartTime, dayShift.EndTime, dayShift.WorkHours, dayShift.ShiftType, dayShift.Id.Value, dayShift.Name)
                    : new ShiftDayDto(d.DayOfWeek, d.StartTime, d.EndTime, d.WorkHours, d.ShiftType, d.DayShiftId?.Value)).ToList(),
                s.RoundingsEnabled,
                s.RoundingInterval,
                s.FlexWindowEndTime,
                s.WeeklyWorkHours,
                s.SecondBlockStartTime,
                s.SecondBlockEndTime,
                s.SecondBlockToleranceMinutes,
                s.PunchTrackingMode,
                s.HasEntryWindow,
                s.PaidLunchMinutes));
    }
}
