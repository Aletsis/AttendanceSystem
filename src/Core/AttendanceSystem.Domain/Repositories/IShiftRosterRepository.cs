using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IShiftRosterRepository
{
    Task<ShiftRoster?> GetByIdAsync(ShiftRosterId id, CancellationToken cancellationToken = default);
    Task<ShiftRoster?> GetByEmployeeAndDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftRoster>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, BranchId? branchId = null, EmployeeId? employeeId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftRoster>> GetByEmployeeAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task AddAsync(ShiftRoster roster, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<ShiftRoster> rosters, CancellationToken cancellationToken = default);
    Task UpdateAsync(ShiftRoster roster, CancellationToken cancellationToken = default);
    void Remove(ShiftRoster roster);
    void RemoveRange(IEnumerable<ShiftRoster> rosters);
}
