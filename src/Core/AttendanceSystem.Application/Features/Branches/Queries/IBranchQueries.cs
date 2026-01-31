using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Features.Branches.Queries;

public interface IBranchQueries
{
    Task<IEnumerable<BranchDto>> GetAllBranchesAsync(CancellationToken cancellationToken = default);
}
