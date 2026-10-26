using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

namespace AttendanceSystem.Domain.Repositories;

public interface IQuarantinedPunchRepository
{
    Task AddRangeAsync(IEnumerable<QuarantinedPunch> punches, CancellationToken cancellationToken = default);
    Task<QuarantinedPunch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Los más recientes primero; <paramref name="status"/> null = todos.</summary>
    Task<IReadOnlyList<QuarantinedPunch>> GetAsync(QuarantineStatus? status, int take, CancellationToken cancellationToken = default);
    /// <summary>Si el mismo marcaje (reloj, empleado, hora reportada) ya está en cuarentena: una descarga repetida no lo duplica.</summary>
    Task<IReadOnlySet<(string EmployeeId, DateTime ReportedTime)>> GetExistingAsync(DeviceId deviceId, IReadOnlyCollection<(string EmployeeId, DateTime ReportedTime)> keys, CancellationToken cancellationToken = default);
}
