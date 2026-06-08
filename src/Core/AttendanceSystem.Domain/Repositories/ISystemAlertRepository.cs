using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface ISystemAlertRepository
{
    Task AddAsync(SystemAlert alert, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(AlertType type, string referenceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<SystemAlert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);
}
