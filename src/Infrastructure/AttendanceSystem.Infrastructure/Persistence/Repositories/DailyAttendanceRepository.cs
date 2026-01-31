using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class DailyAttendanceRepository : IDailyAttendanceRepository
{
    private readonly AttendanceDbContext _context;

    public DailyAttendanceRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<DailyAttendance?> GetByIdAsync(DailyAttendanceId id, CancellationToken cancellationToken = default)
    {
        return await _context.DailyAttendances
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<DailyAttendance?> GetByEmployeeAndDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default)
    {
         return await _context.DailyAttendances
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Date == date.Date, cancellationToken);
    }
    
    public async Task<List<DailyAttendance>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, BranchId? branchId = null, EmployeeId? employeeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.DailyAttendances.AsQueryable();

        query = query.Where(x => x.Date >= startDate.Date && x.Date <= endDate.Date);

        if (employeeId != null)
        {
            query = query.Where(x => x.EmployeeId == employeeId);
        }

        if (branchId != null)
        {
            query = from da in query
                    join emp in _context.Employees on da.EmployeeId equals emp.Id
                    where emp.BranchId == branchId
                    select da;
        }

        return await query
            .OrderBy(x => x.Date)
            .ThenBy(x => x.EmployeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DailyAttendance>> GetByEmployeeAndDateRangeAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await _context.DailyAttendances
             .Where(x => x.EmployeeId == employeeId && x.Date >= startDate.Date && x.Date <= endDate.Date)
             .OrderBy(x => x.Date)
             .ToListAsync(cancellationToken);
    }

    public void Add(DailyAttendance dailyAttendance)
    {
        _context.DailyAttendances.Add(dailyAttendance);
    }

    public void Update(DailyAttendance dailyAttendance)
    {
        _context.DailyAttendances.Update(dailyAttendance);
    }

    public void Remove(DailyAttendance dailyAttendance)
    {
        _context.DailyAttendances.Remove(dailyAttendance);
    }
}
