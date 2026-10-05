using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;

public sealed record SendEmployeeToDeviceCommand(string EmployeeId, string DeviceId, DevicePrivilege? DevicePrivilege = null) : IRequest<Result<bool>>;

public class SendEmployeeToDeviceCommandHandler : IRequestHandler<SendEmployeeToDeviceCommand, Result<bool>>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SendEmployeeToDeviceCommandHandler> _logger;

    public SendEmployeeToDeviceCommandHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository,
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        ILogger<SendEmployeeToDeviceCommandHandler> logger)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(SendEmployeeToDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceRepository.GetByIdAsync(DeviceId.From(request.DeviceId), cancellationToken);
            if (device == null) return Result<bool>.Failure($"Dispositivo {request.DeviceId} no encontrado.");

            // 1. Intentar buscar en empleados internos
            Employee? internalEmployee = null;
            try
            {
                var employeeId = EmployeeId.From(request.EmployeeId);
                internalEmployee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
            }
            catch
            {
                // Si el formato de ID no es válido para EmployeeId, continuamos a buscar en externos
            }

            // 2. Si no es interno, buscar en empleados externos
            ExternalEmployee? externalEmployee = null;
            if (internalEmployee == null)
            {
                if (Guid.TryParse(request.EmployeeId, out var extGuid))
                {
                    externalEmployee = await _externalEmployeeRepository.GetByIdAsync(extGuid, cancellationToken);
                }

                if (externalEmployee == null)
                {
                    var allExternals = await _externalEmployeeRepository.GetAllAsync(cancellationToken);
                    externalEmployee = allExternals.FirstOrDefault(e => e.EmployeeNumber.Equals(request.EmployeeId.Trim(), StringComparison.OrdinalIgnoreCase));
                }
            }

            if (internalEmployee == null && externalEmployee == null)
            {
                return Result<bool>.Failure($"Empleado {request.EmployeeId} no encontrado.");
            }

            var deviceClient = _deviceClientFactory.GetClient(device);

            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            if (!connected)
            {
                return Result<bool>.Failure($"No se pudo conectar al dispositivo {device.Name} ({device.IpAddress}).");
            }

            try
            {
                DeviceUserDto userDto;

                if (internalEmployee != null)
                {
                    if (request.DevicePrivilege.HasValue)
                    {
                        var normalizedPrivilege = DevicePrivilegeMapper.NormalizeForDevice(device.Brand, request.DevicePrivilege.Value);
                        if (internalEmployee.DevicePrivilege != normalizedPrivilege)
                        {
                            internalEmployee.UpdateDevicePrivilege(normalizedPrivilege);
                            _employeeRepository.Update(internalEmployee);
                            await _unitOfWork.SaveChangesAsync(cancellationToken);
                        }
                    }

                    var employeeBranch = await _branchRepository.GetByIdAsync(internalEmployee.BranchId, cancellationToken);
                    string deviceUserId = internalEmployee.Id.Value;

                    if (employeeBranch != null && employeeBranch.IsExternal)
                    {
                        deviceUserId = $"{employeeBranch.Code}{internalEmployee.Id.Value}";
                        _logger.LogInformation("Empleado {Id} pertenece a sucursal externa {Code}. Usando ID concatenado: {DeviceUserId}",
                            internalEmployee.Id.Value, employeeBranch.Code, deviceUserId);
                    }

                    userDto = new DeviceUserDto(
                        deviceUserId,
                        internalEmployee.FirstName,
                        internalEmployee.DevicePassword ?? "",
                        (int)internalEmployee.DevicePrivilege,
                        internalEmployee.Status == EmployeeStatus.Alta,
                        internalEmployee.CardNumber,
                        internalEmployee.Fingerprints?.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.Template)).ToList(),
                        internalEmployee.FaceTemplate,
                        internalEmployee.Photo
                    );
                }
                else
                {
                    // Empleado externo
                    var employeeBranch = await _branchRepository.GetByIdAsync(externalEmployee!.BranchId, cancellationToken);
                    string branchCode = employeeBranch?.Code ?? "";
                    string deviceUserId = $"{branchCode}{externalEmployee.EmployeeNumber}";

                    _logger.LogInformation("Enviando empleado externo {EmpNo} de sucursal {Code} a dispositivo {Device}. ID en reloj: {DeviceUserId}",
                        externalEmployee.EmployeeNumber, branchCode, device.Name, deviceUserId);

                    var privilege = request.DevicePrivilege.HasValue
                        ? (int)DevicePrivilegeMapper.NormalizeForDevice(device.Brand, request.DevicePrivilege.Value)
                        : (int)DevicePrivilege.User;

                    userDto = new DeviceUserDto(
                        deviceUserId,
                        $"{externalEmployee.FirstName} {externalEmployee.LastName}".Trim(),
                        "", // Sin contraseña PIN en sucursal remota
                        privilege,
                        externalEmployee.Status == EmployeeStatus.Alta,
                        externalEmployee.CardNumber,
                        null,
                        null,
                        null
                    );
                }

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
