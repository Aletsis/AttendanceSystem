using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Branches.Commands.UpdateBranch;

[RequiresPermission(Permissions.BranchesManage)]
public sealed record UpdateBranchCommand(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    bool IsExternal,
    string? ExternalHost) : IRequest<Result<Unit>>;

public sealed class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand, Result<Unit>>
{
    private readonly IBranchRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBranchCommandHandler(
        IBranchRepository repository,
        IEmployeeRepository employeeRepository,
        IExternalEmployeeRepository externalEmployeeRepository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _employeeRepository = employeeRepository;
        _externalEmployeeRepository = externalEmployeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit>> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var branch = await _repository.GetByIdAsync(BranchId.From(request.Id), cancellationToken);
            if (branch is null)
                return Result<Unit>.Failure("Sucursal no encontrada");

            // Cambiar entre interna y externa dejaría huérfanos a los empleados del catálogo actual.
            if (branch.IsExternal != request.IsExternal)
            {
                var assigned = branch.IsExternal
                    ? (await _externalEmployeeRepository.GetByBranchAsync(branch.Id, cancellationToken)).Count
                    : (await _employeeRepository.GetByBranchAsync(branch.Id, cancellationToken)).Count;
                if (assigned > 0)
                {
                    var kind = branch.IsExternal ? "empleado(s) externo(s)" : "empleado(s)";
                    return Result<Unit>.Failure(
                        $"No se puede cambiar si la sucursal es externa porque tiene {assigned} {kind} asignados (incluidos los dados de baja).");
                }
            }

            branch.Update(
                request.Code,
                request.Name,
                request.Address,
                request.IsExternal,
                request.ExternalHost);

            _repository.Update(branch);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
