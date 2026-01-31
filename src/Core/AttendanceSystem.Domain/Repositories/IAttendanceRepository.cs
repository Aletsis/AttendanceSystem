namespace AttendanceSystem.Domain.Repositories;

public interface IAttendanceRepository
{
    Task<AttendanceRecord?> GetByIdAsync(
        AttendanceRecordId id, 
        CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<AttendanceRecord>> GetByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        EmployeeId? employeeId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceRecord>> GetByDeviceAndDateRangeAsync(
        DeviceId deviceId,
        DateTime startDateTime,
        DateTime endDateTime,
        CancellationToken cancellationToken = default);
    
    Task AddAsync(
        AttendanceRecord record, 
        CancellationToken cancellationToken = default);
    
    Task AddRangeAsync(
        IEnumerable<AttendanceRecord> records, 
        CancellationToken cancellationToken = default);
    
    Task UpdateAsync(
        AttendanceRecord record, 
        CancellationToken cancellationToken = default);
}