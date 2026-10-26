using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

namespace AttendanceSystem.Domain.Repositories;

/// <summary>Caso 18: incidentes de hora de los relojes.</summary>
public interface IDeviceClockIncidentRepository
{
    Task<IReadOnlyList<DeviceClockIncident>> GetForDeviceAsync(DeviceId deviceId, CancellationToken cancellationToken = default);
    Task AddAsync(DeviceClockIncident incident, CancellationToken cancellationToken = default);
}
