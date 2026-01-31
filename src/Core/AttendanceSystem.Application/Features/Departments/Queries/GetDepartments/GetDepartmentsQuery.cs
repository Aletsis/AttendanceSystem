using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using MediatR;
using AttendanceSystem.Application.Features.Departments.Queries;

namespace AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;

public sealed record GetDepartmentsQuery : IRequest<Result<IEnumerable<DepartmentDto>>>;

public sealed class GetDepartmentsQueryHandler : IRequestHandler<GetDepartmentsQuery, Result<IEnumerable<DepartmentDto>>>
{
    private readonly IDepartmentQueries _queries;

    public GetDepartmentsQueryHandler(IDepartmentQueries queries)
    {
        _queries = queries;
    }

    public async Task<Result<IEnumerable<DepartmentDto>>> Handle(GetDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var result = await _queries.GetAllDepartmentsAsync(cancellationToken);
        return Result<IEnumerable<DepartmentDto>>.Success(result);
    }
}
