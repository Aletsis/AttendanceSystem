using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Queries;

public sealed record GetExternalEmployeeByIdQuery(Guid Id) : IRequest<Result<ExternalEmployeeDto>>;

public sealed class GetExternalEmployeeByIdQueryHandler : IRequestHandler<GetExternalEmployeeByIdQuery, Result<ExternalEmployeeDto>>
{
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;

    public GetExternalEmployeeByIdQueryHandler(
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository)
    {
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
    }

    public async Task<Result<ExternalEmployeeDto>> Handle(GetExternalEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        var employee = await _externalEmployeeRepository.GetByIdAsync(request.Id, cancellationToken);
        if (employee is null)
        {
            return Result<ExternalEmployeeDto>.Failure($"No se encontró el empleado externo con ID {request.Id}");
        }

        var branch = await _branchRepository.GetByIdAsync(employee.BranchId, cancellationToken);

        var dto = new ExternalEmployeeDto
        {
            Id = employee.Id,
            BranchId = employee.BranchId.Value,
            BranchCode = branch?.Code ?? string.Empty,
            BranchName = branch?.Name ?? string.Empty,
            EmployeeNumber = employee.EmployeeNumber,
            FirstName = employee.FirstName,
            LastName = employee.LastName,
            FullName = employee.GetFullName(),
            Email = employee.Email,
            PhoneNumber = employee.PhoneNumber,
            Position = employee.Position,
            Department = employee.Department,
            Status = employee.Status,
            CardNumber = employee.CardNumber,
            CreatedAt = employee.CreatedAt,
            UpdatedAt = employee.UpdatedAt
        };

        return Result<ExternalEmployeeDto>.Success(dto);
    }
}
