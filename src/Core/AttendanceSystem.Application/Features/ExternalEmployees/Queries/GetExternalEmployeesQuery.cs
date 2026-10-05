using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Queries;

public sealed record GetExternalEmployeesQuery(
    string? BranchId = null,
    string? SearchTerm = null) : IRequest<Result<IReadOnlyList<ExternalEmployeeDto>>>;

public sealed class GetExternalEmployeesQueryHandler : IRequestHandler<GetExternalEmployeesQuery, Result<IReadOnlyList<ExternalEmployeeDto>>>
{
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;

    public GetExternalEmployeesQueryHandler(
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository)
    {
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
    }

    public async Task<Result<IReadOnlyList<ExternalEmployeeDto>>> Handle(GetExternalEmployeesQuery request, CancellationToken cancellationToken)
    {
        var branches = (await _branchRepository.GetAllAsync(cancellationToken))
            .ToDictionary(b => b.Id.Value);

        var employees = string.IsNullOrWhiteSpace(request.BranchId)
            ? await _externalEmployeeRepository.GetAllAsync(cancellationToken)
            : await _externalEmployeeRepository.GetByBranchAsync(BranchId.From(request.BranchId), cancellationToken);

        var query = employees.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.EmployeeNumber.ToLowerInvariant().Contains(term) ||
                e.FirstName.ToLowerInvariant().Contains(term) ||
                e.LastName.ToLowerInvariant().Contains(term) ||
                (e.Position != null && e.Position.ToLowerInvariant().Contains(term)) ||
                (e.Department != null && e.Department.ToLowerInvariant().Contains(term)));
        }

        var dtos = query.Select(e =>
        {
            branches.TryGetValue(e.BranchId.Value, out var branch);
            return new ExternalEmployeeDto
            {
                Id = e.Id,
                BranchId = e.BranchId.Value,
                BranchCode = branch?.Code ?? string.Empty,
                BranchName = branch?.Name ?? string.Empty,
                EmployeeNumber = e.EmployeeNumber,
                FirstName = e.FirstName,
                LastName = e.LastName,
                FullName = e.GetFullName(),
                Email = e.Email,
                PhoneNumber = e.PhoneNumber,
                Position = e.Position,
                Department = e.Department,
                Status = e.Status,
                CardNumber = e.CardNumber,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            };
        }).ToList();

        return Result<IReadOnlyList<ExternalEmployeeDto>>.Success(dtos);
    }
}
