namespace AttendanceSystem.Domain.Repositories;

using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;

public interface IEmployeeAuditLogRepository
{
    Task<IReadOnlyList<EmployeeAuditLog>> GetByEmployeeIdAsync(EmployeeId employeeId, CancellationToken cancellationToken = default);
    Task AddAsync(EmployeeAuditLog log, CancellationToken cancellationToken = default);
}
