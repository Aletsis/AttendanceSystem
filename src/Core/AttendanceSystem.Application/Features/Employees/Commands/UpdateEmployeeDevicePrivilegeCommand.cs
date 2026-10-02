using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Employees.Commands;

public sealed record UpdateEmployeeDevicePrivilegeCommand(string EmployeeId, DevicePrivilege DevicePrivilege) : IRequest<Result>;

public sealed class UpdateEmployeeDevicePrivilegeCommandHandler : IRequestHandler<UpdateEmployeeDevicePrivilegeCommand, Result>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateEmployeeDevicePrivilegeCommandHandler> _logger;

    public UpdateEmployeeDevicePrivilegeCommandHandler(
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateEmployeeDevicePrivilegeCommandHandler> logger)
    {
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(UpdateEmployeeDevicePrivilegeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var empId = EmployeeId.From(request.EmployeeId);
            var employee = await _employeeRepository.GetByIdAsync(empId, cancellationToken);
            if (employee is null)
            {
                return Result.Failure($"No existe el empleado con ID {request.EmployeeId}");
            }

            employee.UpdateDevicePrivilege(request.DevicePrivilege);
            _employeeRepository.Update(employee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Privilegio de reloj actualizado para empleado {EmployeeId}: {Privilege}", request.EmployeeId, request.DevicePrivilege);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar privilegio de reloj para empleado {EmployeeId}", request.EmployeeId);
            return Result.Failure("Error al actualizar privilegio de reloj");
        }
    }
}
