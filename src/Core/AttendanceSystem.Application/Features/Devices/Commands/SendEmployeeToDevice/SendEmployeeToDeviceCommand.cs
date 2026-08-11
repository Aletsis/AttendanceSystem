using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;

public sealed record SendEmployeeToDeviceCommand(string EmployeeId, string DeviceId) : IRequest<Result<bool>>;

public class SendEmployeeToDeviceCommandHandler : IRequestHandler<SendEmployeeToDeviceCommand, Result<bool>>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ILogger<SendEmployeeToDeviceCommandHandler> _logger;

    public SendEmployeeToDeviceCommandHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        ILogger<SendEmployeeToDeviceCommandHandler> logger)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(SendEmployeeToDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceRepository.GetByIdAsync(DeviceId.From(request.DeviceId), cancellationToken);
            if (device == null) return Result<bool>.Failure($"Dispositivo {request.DeviceId} no encontrado.");

            var employeeId = EmployeeId.From(request.EmployeeId);
            var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
            if (employee == null) return Result<bool>.Failure($"Empleado {request.EmployeeId} no encontrado.");

            var deviceClient = _deviceClientFactory.GetClient(device);

            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            if (!connected)
            {
                return Result<bool>.Failure($"No se pudo conectar al dispositivo {device.Name} ({device.IpAddress}).");
            }

            try
            {
                var employeeBranch = await _branchRepository.GetByIdAsync(employee.BranchId, cancellationToken);
                string deviceUserId = employee.Id.Value;

                if (employeeBranch != null && employeeBranch.IsExternal)
                {
                    deviceUserId = $"{employeeBranch.Code}{employee.Id.Value}";
                    _logger.LogInformation("Empleado {Id} pertenece a sucursal externa {Code}. Usando ID concatenado: {DeviceUserId}", 
                        employee.Id.Value, employeeBranch.Code, deviceUserId);
                }

                var userDto = new DeviceUserDto(
                    deviceUserId,
                    employee.FirstName,
                    employee.DevicePassword ?? "",
                    (int)employee.DevicePrivilege,
                    employee.Status == EmployeeStatus.Alta,
                    employee.CardNumber,
                    employee.Fingerprints?.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.Template)).ToList(),
                    employee.FaceTemplate,
                    employee.Photo
                );

                var success = await deviceClient.SetUserAsync(userDto, cancellationToken);
                
                if (success)
                    return Result<bool>.Success(true);
                else
                    return Result<bool>.Failure("El dispositivo rechazó la operación o falló la escritura.");
            }
            finally
            {
                await deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando empleado a dispositivo");
            return Result<bool>.Failure(ex.Message);
        }
    }
}
