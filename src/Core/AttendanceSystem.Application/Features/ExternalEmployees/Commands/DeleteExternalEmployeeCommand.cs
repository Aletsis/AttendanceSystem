using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Commands;

public sealed record DeleteExternalEmployeeCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteExternalEmployeeCommandHandler : IRequestHandler<DeleteExternalEmployeeCommand, Result>
{
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteExternalEmployeeCommandHandler> _logger;

    public DeleteExternalEmployeeCommandHandler(
        IExternalEmployeeRepository externalEmployeeRepository,
        IUnitOfWork unitOfWork,
        ILogger<DeleteExternalEmployeeCommandHandler> logger)
    {
        _externalEmployeeRepository = externalEmployeeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteExternalEmployeeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var externalEmployee = await _externalEmployeeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (externalEmployee is null)
            {
                return Result.Failure($"No se encontró el empleado externo con ID {request.Id}");
            }

            _externalEmployeeRepository.Delete(externalEmployee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Empleado externo eliminado: {EmployeeNumber} - {FullName}",
                externalEmployee.EmployeeNumber, externalEmployee.GetFullName());

            return Result.Success();
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Error de dominio al eliminar empleado externo");
            return Result.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al eliminar empleado externo");
            return Result.Failure("Error al eliminar el empleado externo");
        }
    }
}
