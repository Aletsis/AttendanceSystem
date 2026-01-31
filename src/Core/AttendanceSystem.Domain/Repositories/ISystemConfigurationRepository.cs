using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;

namespace AttendanceSystem.Domain.Repositories;

public interface ISystemConfigurationRepository
{
    Task<SystemConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken = default);
    void Add(SystemConfiguration configuration);
    void Update(SystemConfiguration configuration);
}
