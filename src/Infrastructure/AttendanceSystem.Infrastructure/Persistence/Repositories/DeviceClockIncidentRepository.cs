using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public sealed class DeviceClockIncidentRepository : IDeviceClockIncidentRepository
{
    private readonly AttendanceDbContext _context;

    public DeviceClockIncidentRepository(AttendanceDbContext context) => _context = context;

    public async Task<IReadOnlyList<DeviceClockIncident>> GetForDeviceAsync(DeviceId deviceId, CancellationToken cancellationToken = default) =>
        await _context.DeviceClockIncidents.Where(x => x.DeviceId == deviceId).OrderBy(x => x.ReportedFrom).ToListAsync(cancellationToken);

    public Task AddAsync(DeviceClockIncident incident, CancellationToken cancellationToken = default) =>
        _context.DeviceClockIncidents.AddAsync(incident, cancellationToken).AsTask();
}
