using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using MediatR;
using AttendanceSystem.Application.Features.Branches.Queries;

namespace AttendanceSystem.Application.Features.Branches.Queries.GetBranches;

public sealed record GetBranchesQuery : IRequest<Result<IEnumerable<BranchDto>>>;

public sealed class GetBranchesQueryHandler : IRequestHandler<GetBranchesQuery, Result<IEnumerable<BranchDto>>>
{
    private readonly IBranchQueries _queries;

    public GetBranchesQueryHandler(IBranchQueries queries)
    {
        _queries = queries;
    }

    public async Task<Result<IEnumerable<BranchDto>>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        var result = await _queries.GetAllBranchesAsync(cancellationToken);
        return Result<IEnumerable<BranchDto>>.Success(result);
    }
}
