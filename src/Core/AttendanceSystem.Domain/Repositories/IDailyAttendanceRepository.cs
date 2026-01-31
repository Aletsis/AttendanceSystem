using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IDailyAttendanceRepository
{
    Task<DailyAttendance?> GetByIdAsync(DailyAttendanceId id, CancellationToken cancellationToken = default);
    Task<DailyAttendance?> GetByEmployeeAndDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default);
    Task<List<DailyAttendance>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, BranchId? branchId = null, EmployeeId? employeeId = null, CancellationToken cancellationToken = default);
    Task<List<DailyAttendance>> GetByEmployeeAndDateRangeAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    
    void Add(DailyAttendance dailyAttendance);
    void Update(DailyAttendance dailyAttendance);
    void Remove(DailyAttendance dailyAttendance);
}
