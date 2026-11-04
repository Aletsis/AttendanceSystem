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
/// Cancelar detiene el envío entre un empleado y otro, nunca a media escritura (el reloj queda deshabilitado mientras
/// se le escribe un usuario); lo enviado hasta ahí se devuelve con <see cref="SendBranchEmployeesToDeviceResult.Cancelled"/>.
/// </summary>
/// <param name="Progress">Avance para mostrar mientras se envía: puede tardar minutos con sucursales grandes.</param>
[RequiresPermission(Permissions.EmployeesSendToDevice)]
public sealed record SendBranchEmployeesToDeviceCommand(string DeviceId, Guid BranchId, IProgress<SendBranchEmployeesProgress>? Progress = null)
    : IRequest<Result<SendBranchEmployeesToDeviceResult>>;

/// <param name="Done">Empleados ya procesados (enviados o fallidos) de <paramref name="Total"/>.</param>
/// <param name="Message">Lo que se está haciendo ahora, para mostrarlo tal cual.</param>
public sealed record SendBranchEmployeesProgress(int Done, int Total, string Message);

/// <param name="Failed">Nombre e ID en el reloj de los empleados que no se pudieron enviar.</param>
/// <param name="Cancelled">Se detuvo antes de terminar: los que faltaban no se enviaron ni cuentan como fallidos.</param>
public sealed record SendBranchEmployeesToDeviceResult(int Total, int Sent, IReadOnlyList<string> Failed, bool Cancelled = false);

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

        // Una descarga en curso tiene apartado el reloj: sin este aviso la espera parece un envío colgado.
        request.Progress?.Report(new SendBranchEmployeesProgress(0, users.Count, $"Esperando a que {device.Name} se desocupe..."));

        Result<SendBranchEmployeesToDeviceResult> result = Result<SendBranchEmployeesToDeviceResult>.Failure("No se pudo usar el dispositivo");
        await _deviceLock.ExecuteWithLockAsync(device.Id.Value, async () =>
            result = await SendAsync(device, branch.Name, users, request.Progress, cancellationToken), cancellationToken);
        return result;
    }

    private async Task<Result<SendBranchEmployeesToDeviceResult>> SendAsync(
        Device device, string branchName, List<DeviceUserDto> users, IProgress<SendBranchEmployeesProgress>? progress,
        CancellationToken cancellationToken)
    {
        var deviceClient = _deviceClientFactory.GetClient(device);
        try
        {
            progress?.Report(new SendBranchEmployeesProgress(0, users.Count, $"Conectando con {device.Name}..."));
            if (!await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken))
                return Result<SendBranchEmployeesToDeviceResult>.Failure($"No se pudo conectar al dispositivo {device.Name} ({device.IpAddress}).");

            var failed = new List<string>();
            var done = 0;
            foreach (var user in users)
            {
                if (cancellationToken.IsCancellationRequested) break;
                progress?.Report(new SendBranchEmployeesProgress(done, users.Count, $"Enviando a {user.Name} ({user.UserId})"));
                try
                {
                    // Sin el token: cortar a media escritura dejaría al reloj deshabilitado.
                    if (!await deviceClient.SetUserAsync(user, CancellationToken.None))
                        failed.Add($"{user.Name} ({user.UserId})");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enviando el usuario {UserId} al dispositivo {DeviceId}", user.UserId, device.Id.Value);
                    failed.Add($"{user.Name} ({user.UserId})");
                }
                done++;
            }

            var sent = done - failed.Count;
            var cancelled = done < users.Count;
            _logger.LogInformation("Enviados {Sent} de {Count} empleados de la sucursal {Branch} al dispositivo {DeviceId}{Cancelled}",
                sent, users.Count, branchName, device.Id.Value, cancelled ? " (cancelado)" : "");
            return Result<SendBranchEmployeesToDeviceResult>.Success(new SendBranchEmployeesToDeviceResult(users.Count, sent, failed, cancelled));
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
