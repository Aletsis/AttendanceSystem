using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Commands;

public sealed record UpdateExternalEmployeeCommand(
    Guid Id,
    string BranchId,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? Position,
    string? Department,
    EmployeeStatus Status,
    string? CardNumber = null) : IRequest<Result<ExternalEmployeeDto>>;

public sealed class UpdateExternalEmployeeCommandHandler : IRequestHandler<UpdateExternalEmployeeCommand, Result<ExternalEmployeeDto>>
{
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateExternalEmployeeCommandHandler> _logger;

    public UpdateExternalEmployeeCommandHandler(
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateExternalEmployeeCommandHandler> logger)
    {
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ExternalEmployeeDto>> Handle(UpdateExternalEmployeeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var externalEmployee = await _externalEmployeeRepository.GetByIdAsync(request.Id, cancellationToken);
            if (externalEmployee is null)
            {
                return Result<ExternalEmployeeDto>.Failure($"No se encontró el empleado externo con ID {request.Id}");
            }

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

            if (await _externalEmployeeRepository.ExistsInBranchAsync(branchId, request.EmployeeNumber.Trim(), request.Id, cancellationToken))
            {
                return Result<ExternalEmployeeDto>.Failure($"Ya existe otro empleado con el número '{request.EmployeeNumber}' en la sucursal externa '{branch.Name}'.");
            }

            externalEmployee.Update(
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

            _externalEmployeeRepository.Update(externalEmployee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Empleado externo actualizado: {EmployeeNumber} - {FullName}",
                externalEmployee.EmployeeNumber, externalEmployee.GetFullName());

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
            _logger.LogWarning(ex, "Error de dominio al actualizar empleado externo");
            return Result<ExternalEmployeeDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al actualizar empleado externo");
            return Result<ExternalEmployeeDto>.Failure("Error al actualizar el empleado externo");
        }
    }
}
