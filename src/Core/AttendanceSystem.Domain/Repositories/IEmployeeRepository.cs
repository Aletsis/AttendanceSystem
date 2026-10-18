using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Employee?> GetByIdAsync(EmployeeId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetByBranchAsync(BranchId branchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetByDepartmentAsync(DepartmentId departmentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(EmployeeId id, CancellationToken cancellationToken = default);
    void Add(Employee employee);
    void Update(Employee employee);
    void Delete(Employee employee);
    Task<bool> IsShiftInUseAsync(ShiftId shiftId, CancellationToken cancellationToken = default);
    Task<bool> IsDepartmentInUseAsync(DepartmentId departmentId, CancellationToken cancellationToken = default);
    Task<bool> IsPositionInUseAsync(PositionId positionId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPaginatedAsync(
        int pageNumber,
        int pageSize,
        string? searchString,
        string? sortLabel,
        int? sortDirection,
        string? idFilter = null,
        string? nameFilter = null,
        IEnumerable<string>? branchFilter = null,
        IEnumerable<string>? departmentFilter = null,
        IEnumerable<string>? positionFilter = null,
        IEnumerable<string>? statusFilter = null,
        CancellationToken cancellationToken = default);
}
