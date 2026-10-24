using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;

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

    public async Task<AttendanceRecord?> GetByEmployeeAndCheckTimeAsync(
        EmployeeId employeeId,
        DateTime checkTime,
        CancellationToken cancellationToken = default)
    {
        var utcCheckTime = checkTime.Kind == DateTimeKind.Utc
            ? checkTime
            : (checkTime.Kind == DateTimeKind.Local ? checkTime.ToUniversalTime() : DateTime.SpecifyKind(checkTime, DateTimeKind.Utc));

        return await _context.AttendanceRecords
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.CheckTime == utcCheckTime, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByIdentitiesAsync(
        IReadOnlyCollection<(EmployeeId EmployeeId, DateTime CheckTimeUtc)> identities,
        CancellationToken cancellationToken = default)
    {
        if (identities.Count == 0) return new List<AttendanceRecord>();

        var keys = identities
            .Select(i => (i.EmployeeId.Value, ToUtc(i.CheckTimeUtc)))
            .ToHashSet();
        var employeeIds = identities.Select(i => i.EmployeeId).Distinct().ToList();
        var min = keys.Min(k => k.Item2);
        var max = keys.Max(k => k.Item2);

        // Filtro grueso en SQL (índice EmployeeId+CheckTime) y exacto en memoria.
        var candidates = await _context.AttendanceRecords
            .Where(x => employeeIds.Contains(x.EmployeeId) && x.CheckTime >= min && x.CheckTime <= max)
            .ToListAsync(cancellationToken);

        return candidates.Where(r => keys.Contains((r.EmployeeId.Value, ToUtc(r.CheckTime)))).ToList();
    }

    public void Detach(AttendanceRecord record)
    {
        _context.Entry(record).State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<PunchDaySummary>> GetPunchDaySummaryAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await _context.AttendanceRecords.AsNoTracking()
            .Where(r => r.VoidedAt == null && r.WorkDate.HasValue && r.WorkDate.Value >= fromDate && r.WorkDate.Value <= toDate)
            .GroupBy(r => new { r.EmployeeId, WorkDate = r.WorkDate!.Value, r.DeviceId })
            .Select(g => new { g.Key.EmployeeId, g.Key.WorkDate, g.Key.DeviceId, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new PunchDaySummary(r.EmployeeId, r.WorkDate, r.DeviceId, r.Count)).ToList();
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public async Task<IReadOnlyList<AttendanceRecord>> GetByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        EmployeeId? employeeId = null,
        CancellationToken cancellationToken = default)
    {
        var expandedStartUtc = startDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var expandedEndUtc = endDate.AddDays(1).ToDateTime(TimeOnly.MaxValue);

        var query = _context.AttendanceRecords
            .Where(x => (x.WorkDate.HasValue && x.WorkDate.Value >= startDate && x.WorkDate.Value <= endDate)
                     || (!x.WorkDate.HasValue && x.CheckTime >= expandedStartUtc && x.CheckTime <= expandedEndUtc));

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

    public async Task<IReadOnlyList<AttendanceRecord>> GetByDownloadLogIdAsync(
        DownloadLogId downloadLogId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceRecords
            .Where(x => x.DownloadLogId == downloadLogId)
            .OrderByDescending(x => x.CheckTime)
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
        // F07-07: una entidad ya seguida no se marca entera como modificada; el detector de cambios escribe sólo lo
        // que cambió (nada, si el recálculo deja el marcaje igual). Las entidades sueltas sí se adjuntan.
        if (_context.Entry(record).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            _context.AttendanceRecords.Update(record);
        }
        return Task.CompletedTask;
    }

    public async Task<bool> HasCheckInForDateAsync(
        EmployeeId employeeId,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var targetDate = DateOnly.FromDateTime(date);
        var expandedStartUtc = targetDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var expandedEndUtc = targetDate.AddDays(1).ToDateTime(TimeOnly.MaxValue);

        return await _context.AttendanceRecords
            .AnyAsync(r => r.EmployeeId == employeeId &&
                           r.CheckType == AttendanceSystem.Domain.Enumerations.CheckType.CheckIn &&
                           ((r.WorkDate.HasValue && r.WorkDate.Value == targetDate) ||
                            (!r.WorkDate.HasValue && r.CheckTime >= expandedStartUtc && r.CheckTime <= expandedEndUtc)),
                      cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceRecords.ToListAsync(cancellationToken);
    }
}
