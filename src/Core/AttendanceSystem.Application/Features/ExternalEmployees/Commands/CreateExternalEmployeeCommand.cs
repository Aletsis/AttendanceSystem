using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Commands;

public sealed record CreateExternalEmployeeCommand(
    string BranchId,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? Email = null,
    string? PhoneNumber = null,
    string? Position = null,
    string? Department = null,
    EmployeeStatus Status = EmployeeStatus.Alta,
    string? CardNumber = null) : IRequest<Result<ExternalEmployeeDto>>;

public sealed class CreateExternalEmployeeCommandHandler : IRequestHandler<CreateExternalEmployeeCommand, Result<ExternalEmployeeDto>>
{
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateExternalEmployeeCommandHandler> _logger;

    public CreateExternalEmployeeCommandHandler(
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        ILogger<CreateExternalEmployeeCommandHandler> logger)
    {
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ExternalEmployeeDto>> Handle(CreateExternalEmployeeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var branchId = BranchId.From(request.BranchId);
            var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
            if (branch is null)
            {
                return Result<ExternalEmployeeDto>.Failure($"No existe la sucursal con ID {request.BranchId}");
            }

            if (!branch.IsExternal)
            {
                return Result<ExternalEmployeeDto>.Failure($"La sucursal '{branch.Name}' no está configurada como sucursal externa.");
            }

            if (await _externalEmployeeRepository.ExistsInBranchAsync(branchId, request.EmployeeNumber.Trim(), null, cancellationToken))
            {
                return Result<ExternalEmployeeDto>.Failure($"Ya existe un empleado con el número '{request.EmployeeNumber}' en la sucursal externa '{branch.Name}'.");
            }

            var externalEmployee = ExternalEmployee.Create(
                branchId,
                request.EmployeeNumber,
                request.FirstName,
                request.LastName,
                request.Email,
                request.PhoneNumber,
                request.Position,
                request.Department,
                request.Status,
                request.CardNumber);

            _externalEmployeeRepository.Add(externalEmployee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Empleado externo creado: {EmployeeNumber} - {FullName} en {BranchName}",
                externalEmployee.EmployeeNumber, externalEmployee.GetFullName(), branch.Name);

            var dto = new ExternalEmployeeDto
            {
                Id = externalEmployee.Id,
                BranchId = branch.Id.Value,
                BranchCode = branch.Code,
                BranchName = branch.Name,
                EmployeeNumber = externalEmployee.EmployeeNumber,
                FirstName = externalEmployee.FirstName,
                LastName = externalEmployee.LastName,
                FullName = externalEmployee.GetFullName(),
                Email = externalEmployee.Email,
                PhoneNumber = externalEmployee.PhoneNumber,
                Position = externalEmployee.Position,
                Department = externalEmployee.Department,
                Status = externalEmployee.Status,
                CardNumber = externalEmployee.CardNumber,
                CreatedAt = externalEmployee.CreatedAt,
                UpdatedAt = externalEmployee.UpdatedAt
            };

            return Result<ExternalEmployeeDto>.Success(dto);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Error de dominio al crear empleado externo");
            return Result<ExternalEmployeeDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al crear empleado externo");
            return Result<ExternalEmployeeDto>.Failure("Error al registrar el empleado externo");
        }
    }
}
