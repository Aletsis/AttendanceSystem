using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

namespace AttendanceSystem.Domain.Repositories;

public interface IPunchTimeCorrectionRepository
{
    /// <summary>Marcajes del reloj cuya fecha laboral cae en el rango (con seguimiento, para corregirlos).</summary>
    Task<IReadOnlyList<AttendanceRecord>> GetDeviceRecordsAsync(DeviceId deviceId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task AddAsync(PunchTimeCorrection correction, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PunchTimeCorrection>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}
