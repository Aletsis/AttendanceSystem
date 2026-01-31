using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(PositionId id, CancellationToken cancellationToken = default);
    Task AddAsync(Position position, CancellationToken cancellationToken = default);
    void Update(Position position);
    void Delete(Position position);
    Task<IEnumerable<Position>> GetAllAsync(CancellationToken cancellationToken = default);
}
