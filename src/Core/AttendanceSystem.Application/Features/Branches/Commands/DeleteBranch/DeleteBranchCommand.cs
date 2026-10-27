using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Branches.Commands.DeleteBranch;

/// <summary>
/// Elimina una sucursal sin uso. Con empleados (de cualquier estatus) o empleados externos asignados se rechaza e
/// indica cuántos la usan: sus registros e historial dependen de ella.
/// </summary>
[RequiresPermission(Permissions.BranchesManage)]
public sealed record DeleteBranchCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteBranchCommandHandler : IRequestHandler<DeleteBranchCommand, Result>
{
    private readonly IBranchRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteBranchCommandHandler(
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

    public async Task<Result> Handle(DeleteBranchCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var branchId = BranchId.From(request.Id);
            var branch = await _repository.GetByIdAsync(branchId, cancellationToken);
            if (branch == null)
            {
                return Result.Failure("Sucursal no encontrada.");
            }

            var employees = (await _employeeRepository.GetByBranchAsync(branchId, cancellationToken)).Count;
            var externalEmployees = (await _externalEmployeeRepository.GetByBranchAsync(branchId, cancellationToken)).Count;
            if (employees > 0 || externalEmployees > 0)
            {
                var uses = new List<string>();
                if (employees > 0) uses.Add($"{employees} empleado(s)");
                if (externalEmployees > 0) uses.Add($"{externalEmployees} empleado(s) externo(s)");
                return Result.Failure($"No se puede eliminar la sucursal porque tiene {string.Join(" y ", uses)} asignados (incluidos los dados de baja).");
            }

            _repository.Delete(branch);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
