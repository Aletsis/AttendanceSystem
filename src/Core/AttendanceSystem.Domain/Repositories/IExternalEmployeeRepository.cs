using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IExternalEmployeeRepository
{
    Task<IReadOnlyList<ExternalEmployee>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ExternalEmployee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalEmployee>> GetByBranchAsync(BranchId branchId, CancellationToken cancellationToken = default);
    Task<ExternalEmployee?> GetByBranchAndEmployeeNumberAsync(BranchId branchId, string employeeNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsInBranchAsync(BranchId branchId, string employeeNumber, Guid? excludeId = null, CancellationToken cancellationToken = default);
    void Add(ExternalEmployee externalEmployee);
    void Update(ExternalEmployee externalEmployee);
    void Delete(ExternalEmployee externalEmployee);
}
