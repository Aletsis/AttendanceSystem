using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class ShiftRosterRepository : IShiftRosterRepository
{
    private readonly AttendanceDbContext _context;

    public ShiftRosterRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<ShiftRoster?> GetByIdAsync(ShiftRosterId id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<ShiftRoster>()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<ShiftRoster?> GetByEmployeeAndDateAsync(
        EmployeeId employeeId,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var targetDate = date.Date;
        return await _context.Set<ShiftRoster>()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == targetDate, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftRoster>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        BranchId? branchId = null,
        EmployeeId? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        var start = startDate.Date;
        var end = endDate.Date;

        var query = _context.Set<ShiftRoster>()
            .Where(r => r.Date >= start && r.Date <= end);

        if (employeeId != null)
        {
            query = query.Where(r => r.EmployeeId == employeeId);
        }
        else if (branchId != null)
        {
            var branchEmployeeIds = _context.Employees
                .Where(e => e.BranchId == branchId)
                .Select(e => e.Id);

            query = query.Where(r => branchEmployeeIds.Contains(r.EmployeeId));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftRoster>> GetByEmployeeAsync(
        EmployeeId employeeId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var start = startDate.Date;
        var end = endDate.Date;

        return await _context.Set<ShiftRoster>()
            .Where(r => r.EmployeeId == employeeId && r.Date >= start && r.Date <= end)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftRoster>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<ShiftRoster>().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeId>> GetEmployeeIdsWithRosterAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<ShiftRoster>()
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ShiftRoster roster, CancellationToken cancellationToken = default)
    {
        await _context.Set<ShiftRoster>().AddAsync(roster, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<ShiftRoster> rosters, CancellationToken cancellationToken = default)
    {
        await _context.Set<ShiftRoster>().AddRangeAsync(rosters, cancellationToken);
    }

    public Task UpdateAsync(ShiftRoster roster, CancellationToken cancellationToken = default)
    {
        _context.Set<ShiftRoster>().Update(roster);
        return Task.CompletedTask;
    }

    public void Remove(ShiftRoster roster)
    {
        _context.Set<ShiftRoster>().Remove(roster);
    }

    public void RemoveRange(IEnumerable<ShiftRoster> rosters)
    {
        _context.Set<ShiftRoster>().RemoveRange(rosters);
    }
}
