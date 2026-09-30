using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;

namespace AttendanceSystem.Domain.Repositories;

public interface IExternalAttendanceLogRepository
{
    Task AddAsync(ExternalAttendanceLog log, CancellationToken cancellationToken = default);
    Task UpdateAsync(ExternalAttendanceLog log, CancellationToken cancellationToken = default);
    Task<ExternalAttendanceLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalAttendanceLog>> GetPendingOrFailedLogsAsync(int maxCount = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalAttendanceLog>> GetRecentLogsAsync(int take = 50, CancellationToken cancellationToken = default);
}
