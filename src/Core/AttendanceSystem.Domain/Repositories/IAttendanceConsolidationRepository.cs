using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IAttendanceConsolidationRepository
{
    Task<AttendanceConsolidationRequest?> GetPendingByEmployeeAsync(
        EmployeeId employeeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceConsolidationRequest>> GetPendingBatchAsync(
        int maxBatchSize,
        CancellationToken cancellationToken = default);

    Task AddAsync(AttendanceConsolidationRequest request, CancellationToken cancellationToken = default);
    void Update(AttendanceConsolidationRequest request);
}
