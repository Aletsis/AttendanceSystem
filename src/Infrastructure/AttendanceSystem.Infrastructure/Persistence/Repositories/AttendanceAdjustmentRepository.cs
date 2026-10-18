using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class AttendanceAdjustmentRepository : IAttendanceAdjustmentRepository
{
    private readonly AttendanceDbContext _context;

    public AttendanceAdjustmentRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<AttendanceAdjustment?> GetByEmployeeAndDateAsync(
        EmployeeId employeeId,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceAdjustments
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Date == date.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceAdjustment>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        BranchId? branchId = null,
        EmployeeId? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AttendanceAdjustments.AsQueryable();

        query = query.Where(x => x.Date >= startDate.Date && x.Date <= endDate.Date);

        if (employeeId != null)
        {
            query = query.Where(x => x.EmployeeId == employeeId);
        }

        if (branchId != null)
        {
            query = from adj in query
                    join emp in _context.Employees on adj.EmployeeId equals emp.Id
                    where emp.BranchId == branchId
                    select adj;
        }

        return await query
            .OrderBy(x => x.Date)
            .ThenBy(x => x.EmployeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(AttendanceAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        await _context.AttendanceAdjustments.AddAsync(adjustment, cancellationToken);
    }

    public void Update(AttendanceAdjustment adjustment)
    {
        _context.AttendanceAdjustments.Update(adjustment);
    }

    public void Remove(AttendanceAdjustment adjustment)
    {
        _context.AttendanceAdjustments.Remove(adjustment);
    }
}
