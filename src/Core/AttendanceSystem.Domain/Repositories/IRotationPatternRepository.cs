using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IRotationPatternRepository
{
    Task<RotationPattern?> GetByEmployeeAsync(EmployeeId employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RotationPattern>> GetIndefiniteAsync(CancellationToken cancellationToken = default);
    Task AddAsync(RotationPattern pattern, CancellationToken cancellationToken = default);
}
