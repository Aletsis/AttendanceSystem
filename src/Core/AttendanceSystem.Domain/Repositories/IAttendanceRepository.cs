using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;

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
    Task<IReadOnlyList<AttendanceRecord>> GetByDownloadLogIdAsync(
        DownloadLogId downloadLogId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// F03-01: identidad técnica del marcaje = (EmployeeId, CheckTime UTC), sin dispositivo ni fuente.
    /// Devuelve los registros existentes que coinciden con alguna de las identidades indicadas.
    /// </summary>
    Task<IReadOnlyList<AttendanceRecord>> GetByIdentitiesAsync(
        IReadOnlyCollection<(EmployeeId EmployeeId, DateTime CheckTimeUtc)> identities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deja de rastrear un registro pendiente que no debe persistirse (p. ej. colisión confirmada).
    /// </summary>
    void Detach(AttendanceRecord record);

    /// <summary>Caso 13: borra un marcaje (sólo los manuales se borran).</summary>
    void Remove(AttendanceRecord record);

    Task<AttendanceRecord?> GetByEmployeeAndCheckTimeAsync(
        EmployeeId employeeId,
        DateTime checkTime,
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

    Task<bool> HasCheckInForDateAsync(
        EmployeeId employeeId,
        DateTime date,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceRecord>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
