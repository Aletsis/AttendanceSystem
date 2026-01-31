using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using MediatR;
using AttendanceSystem.Application.Features.Departments.Queries;

namespace AttendanceSystem.Application.Features.Departments.Queries.GetDepartmentById;

public sealed record GetDepartmentByIdQuery(Guid Id) : IRequest<Result<DepartmentDto>>;

public sealed class GetDepartmentByIdQueryHandler : IRequestHandler<GetDepartmentByIdQuery, Result<DepartmentDto>>
{
    private readonly IDepartmentQueries _queries;

    public GetDepartmentByIdQueryHandler(IDepartmentQueries queries)
    {
        _queries = queries;
    }

    public async Task<Result<DepartmentDto>> Handle(GetDepartmentByIdQuery request, CancellationToken cancellationToken)
    {
        var result = await _queries.GetDepartmentByIdAsync(request.Id, cancellationToken);
        if (result == null)
            return Result<DepartmentDto>.Failure("Departamento no encontrado");
            
        return Result<DepartmentDto>.Success(result);
    }
}
