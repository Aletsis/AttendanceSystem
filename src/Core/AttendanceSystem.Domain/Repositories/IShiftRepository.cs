using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IShiftRepository
{
    Task<Shift?> GetByIdAsync(ShiftId id, CancellationToken cancellationToken = default);
    Task AddAsync(Shift shift, CancellationToken cancellationToken = default);
    void Update(Shift shift);
    void Delete(Shift shift);
    Task<IEnumerable<Shift>> GetAllAsync(CancellationToken cancellationToken = default);
}
