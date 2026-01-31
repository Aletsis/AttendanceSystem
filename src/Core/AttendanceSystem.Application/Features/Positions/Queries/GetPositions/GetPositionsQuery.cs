using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using MediatR;
using AttendanceSystem.Application.Features.Positions.Queries;

namespace AttendanceSystem.Application.Features.Positions.Queries.GetPositions;

public sealed record GetPositionsQuery : IRequest<Result<IEnumerable<PositionDto>>>;

public sealed class GetPositionsQueryHandler : IRequestHandler<GetPositionsQuery, Result<IEnumerable<PositionDto>>>
{
    private readonly IPositionQueries _queries;

    public GetPositionsQueryHandler(IPositionQueries queries)
    {
        _queries = queries;
    }

    public async Task<Result<IEnumerable<PositionDto>>> Handle(GetPositionsQuery request, CancellationToken cancellationToken)
    {
        var result = await _queries.GetAllPositionsAsync(cancellationToken);
        return Result<IEnumerable<PositionDto>>.Success(result);
    }
}
