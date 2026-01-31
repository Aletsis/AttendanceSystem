using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;

public sealed record SendEmployeeToDeviceCommand(string EmployeeId, string DeviceId) : IRequest<Result<bool>>;

public class SendEmployeeToDeviceCommandHandler : IRequestHandler<SendEmployeeToDeviceCommand, Result<bool>>
{
    private readonly IZKTecoDeviceClient _zkClient;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ILogger<SendEmployeeToDeviceCommandHandler> _logger;

    public SendEmployeeToDeviceCommandHandler(
        IZKTecoDeviceClient zkClient,
        IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository,
        ILogger<SendEmployeeToDeviceCommandHandler> logger)
    {
        _zkClient = zkClient;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
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

            if (!await _zkClient.ConnectAsync(device.IpAddress, device.Port, cancellationToken))
            {
                return Result<bool>.Failure($"No se pudo conectar al dispositivo {device.Name} ({device.IpAddress}).");
            }

            try
            {
                var userDto = new DeviceUserDto(
                    employee.Id.Value,
                    employee.GetFullName(),
                    employee.DevicePassword ?? "",
                    0, // Privilege default, maybe add property to Employee?
                    employee.Status == Domain.Enumerations.EmployeeStatus.Alta,
                    employee.CardNumber,
                    employee.Fingerprints?.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.Template)).ToList(),
                    employee.FaceTemplate
                );

                var success = await _zkClient.SetUserAsync(userDto, cancellationToken);
                
                if (success)
                    return Result<bool>.Success(true);
                else
                    return Result<bool>.Failure("El dispositivo rechazó la operación o falló la escritura.");
            }
            finally
            {
                await _zkClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando empleado a dispositivo");
            return Result<bool>.Failure(ex.Message);
        }
    }
}
