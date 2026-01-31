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
}
