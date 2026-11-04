using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.SendBranchEmployeesToDevice;

/// <summary>
/// Envía a un reloj a todos los empleados de una sucursal (internos y, si la sucursal es externa, sus empleados
/// externos), para dar de alta un reloj nuevo sin enviarlos uno por uno. Los dados de baja no se envían. Una sola
/// conexión con el reloj apartado; un empleado que el reloj rechaza no detiene a los demás. Cada empleado conserva su
/// privilegio. Falla sólo si no se pudo usar el reloj.
/// </summary>
[RequiresPermission(Permissions.EmployeesSendToDevice)]
public sealed record SendBranchEmployeesToDeviceCommand(string DeviceId, Guid BranchId) : IRequest<Result<SendBranchEmployeesToDeviceResult>>;

/// <param name="Failed">Nombre e ID en el reloj de los empleados que no se pudieron enviar.</param>
public sealed record SendBranchEmployeesToDeviceResult(int Total, int Sent, IReadOnlyList<string> Failed);

public sealed class SendBranchEmployeesToDeviceCommandHandler
    : IRequestHandler<SendBranchEmployeesToDeviceCommand, Result<SendBranchEmployeesToDeviceResult>>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDeviceLockService _deviceLock;
    private readonly ILogger<SendBranchEmployeesToDeviceCommandHandler> _logger;

    public SendBranchEmployeesToDeviceCommandHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository,
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository,
        IDeviceLockService deviceLock,
        ILogger<SendBranchEmployeesToDeviceCommandHandler> logger)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
        _deviceLock = deviceLock;
        _logger = logger;
    }

    public async Task<Result<SendBranchEmployeesToDeviceResult>> Handle(SendBranchEmployeesToDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(DeviceId.From(request.DeviceId), cancellationToken);
        if (device == null || !device.IsActive)
            return Result<SendBranchEmployeesToDeviceResult>.Failure("Dispositivo no encontrado o inactivo");

        var branchId = BranchId.From(request.BranchId);
        var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
        if (branch == null)
            return Result<SendBranchEmployeesToDeviceResult>.Failure("Sucursal no encontrada");

        var users = (await _employeeRepository.GetByBranchAsync(branchId, cancellationToken))
            .Where(e => e.Status != EmployeeStatus.Baja)
            .Select(e => DeviceUserMapper.FromEmployee(e, branch))
            .ToList();

        if (branch.IsExternal)
        {
            var privilege = (int)DevicePrivilege.User;
            users.AddRange((await _externalEmployeeRepository.GetByBranchAsync(branchId, cancellationToken))
                .Where(e => e.Status != EmployeeStatus.Baja)
                .Select(e => DeviceUserMapper.FromExternal(e, branch, privilege)));
        }

        if (users.Count == 0)
            return Result<SendBranchEmployeesToDeviceResult>.Success(new SendBranchEmployeesToDeviceResult(0, 0, Array.Empty<string>()));

        Result<SendBranchEmployeesToDeviceResult> result = Result<SendBranchEmployeesToDeviceResult>.Failure("No se pudo usar el dispositivo");
        await _deviceLock.ExecuteWithLockAsync(device.Id.Value, async () =>
            result = await SendAsync(device, branch.Name, users, cancellationToken), cancellationToken);
        return result;
    }

    private async Task<Result<SendBranchEmployeesToDeviceResult>> SendAsync(
        Device device, string branchName, List<DeviceUserDto> users, CancellationToken cancellationToken)
    {
        var deviceClient = _deviceClientFactory.GetClient(device);
        try
        {
            if (!await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken))
                return Result<SendBranchEmployeesToDeviceResult>.Failure($"No se pudo conectar al dispositivo {device.Name} ({device.IpAddress}).");

            var failed = new List<string>();
            foreach (var user in users)
            {
                try
                {
                    if (!await deviceClient.SetUserAsync(user, cancellationToken))
                        failed.Add($"{user.Name} ({user.UserId})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error enviando el usuario {UserId} al dispositivo {DeviceId}", user.UserId, device.Id.Value);
                    failed.Add($"{user.Name} ({user.UserId})");
                }
            }

            var sent = users.Count - failed.Count;
            _logger.LogInformation("Enviados {Sent} de {Count} empleados de la sucursal {Branch} al dispositivo {DeviceId}",
                sent, users.Count, branchName, device.Id.Value);
            return Result<SendBranchEmployeesToDeviceResult>.Success(new SendBranchEmployeesToDeviceResult(users.Count, sent, failed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error enviando empleados de la sucursal {Branch} al dispositivo {DeviceId}", branchName, device.Id.Value);
            return Result<SendBranchEmployeesToDeviceResult>.Failure($"Error enviando empleados: {ex.Message}");
        }
        finally
        {
            try
            {
                await deviceClient.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo desconectar del dispositivo {DeviceId}", device.Id.Value);
            }
        }
    }
}
