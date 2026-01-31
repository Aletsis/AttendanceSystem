using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class AttendanceRepository : IAttendanceRepository
{
    private readonly AttendanceDbContext _context;

    public AttendanceRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public async Task<AttendanceRecord?> GetByIdAsync(
        AttendanceRecordId id, 
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceRecords
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        EmployeeId? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var query = _context.AttendanceRecords
            .Where(x => x.CheckTime >= startDateTime 
                     && x.CheckTime <= endDateTime);

        if (employeeId != null)
        {
            query = query.Where(x => x.EmployeeId == employeeId);
        }

        return await query
            .OrderByDescending(x => x.CheckTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByDeviceAndDateRangeAsync(
        DeviceId deviceId,
        DateTime startDateTime,
        DateTime endDateTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceRecords
            .Where(x => x.DeviceId == deviceId 
                     && x.CheckTime >= startDateTime 
                     && x.CheckTime <= endDateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        AttendanceRecord record, 
        CancellationToken cancellationToken = default)
    {
        await _context.AttendanceRecords.AddAsync(record, cancellationToken);
    }

    public async Task AddRangeAsync(
        IEnumerable<AttendanceRecord> records, 
        CancellationToken cancellationToken = default)
    {
        await _context.AttendanceRecords.AddRangeAsync(records, cancellationToken);
    }

    public Task UpdateAsync(
        AttendanceRecord record, 
        CancellationToken cancellationToken = default)
    {
        _context.AttendanceRecords.Update(record);
        return Task.CompletedTask;
    }
}