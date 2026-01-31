using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IBranchRepository
{
    Task<Branch?> GetByIdAsync(BranchId id, CancellationToken cancellationToken = default);
    Task AddAsync(Branch branch, CancellationToken cancellationToken = default);
    void Update(Branch branch);
    void Delete(Branch branch);
    Task<IEnumerable<Branch>> GetAllAsync(CancellationToken cancellationToken = default);
}
