using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public sealed class PunchTimeCorrectionRepository : IPunchTimeCorrectionRepository
{
    private readonly AttendanceDbContext _context;

    public PunchTimeCorrectionRepository(AttendanceDbContext context) => _context = context;

    public async Task<IReadOnlyList<AttendanceRecord>> GetDeviceRecordsAsync(DeviceId deviceId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
        => await _context.AttendanceRecords
            .Where(r => r.DeviceId == deviceId && r.WorkDate >= fromDate && r.WorkDate <= toDate && r.VoidedAt == null)
            .OrderBy(r => r.CheckTime)
            .ToListAsync(cancellationToken);

    public Task AddAsync(PunchTimeCorrection correction, CancellationToken cancellationToken = default)
        => _context.PunchTimeCorrections.AddAsync(correction, cancellationToken).AsTask();

    public async Task<IReadOnlyList<PunchTimeCorrection>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
        => await _context.PunchTimeCorrections.AsNoTracking()
            .Include(c => c.Entries)
            .OrderByDescending(c => c.AppliedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
