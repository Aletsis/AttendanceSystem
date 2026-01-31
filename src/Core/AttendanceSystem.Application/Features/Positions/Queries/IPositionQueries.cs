using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Features.Positions.Queries;

public interface IPositionQueries
{
    Task<IEnumerable<PositionDto>> GetAllPositionsAsync(CancellationToken cancellationToken = default);
}
