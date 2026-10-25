using AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;

namespace AttendanceSystem.Domain.Repositories;

/// <summary>Caso 17: cierres de periodo (vigentes y reabiertos).</summary>
public interface IAttendancePeriodClosureRepository
{
    Task<IReadOnlyList<AttendancePeriodClosure>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(AttendancePeriodClosure closure, CancellationToken cancellationToken = default);
    void Update(AttendancePeriodClosure closure);
}
