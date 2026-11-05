using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.ExternalEmployees.Services;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Commands.SyncExternalEmployeeBiometrics;

/// <summary>
/// Copia del reloj las huellas (y la tarjeta, si falta) de los empleados externos que aún no tienen huellas, por su ID en
/// el reloj (código de sucursal + número). Como <c>SyncEmployeeBiometricsCommand</c>: una sola conexión por reloj, con el
/// reloj apartado; un empleado que falla no detiene a los demás. Devuelve cuántos empleados se actualizaron.
/// </summary>
[RequiresPermission(Permissions.EmployeesSendToDevice)]
public sealed record SyncExternalEmployeeBiometricsCommand(string DeviceId, IReadOnlyList<string> DeviceUserIds) : IRequest<Result<int>>;

public sealed class SyncExternalEmployeeBiometricsCommandHandler : IRequestHandler<SyncExternalEmployeeBiometricsCommand, Result<int>>
{
    private readonly IExternalEmployeeRepository _externalEmployeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceLockService _deviceLock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncExternalEmployeeBiometricsCommandHandler> _logger;

    public SyncExternalEmployeeBiometricsCommandHandler(
        IExternalEmployeeRepository externalEmployeeRepository,
        IBranchRepository branchRepository,
        IDeviceRepository deviceRepository,
        IDeviceClientFactory deviceClientFactory,
        IDeviceLockService deviceLock,
        IUnitOfWork unitOfWork,
        ILogger<SyncExternalEmployeeBiometricsCommandHandler> logger)
    {
        _externalEmployeeRepository = externalEmployeeRepository;
        _branchRepository = branchRepository;
        _deviceRepository = deviceRepository;
        _deviceClientFactory = deviceClientFactory;
        _deviceLock = deviceLock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Sin huellas: lo único que este comando viene a completar.</summary>
    public static bool IsMissingFingerprints(ExternalEmployee employee) => !employee.Fingerprints.Any();

    public async Task<Result<int>> Handle(SyncExternalEmployeeBiometricsCommand request, CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(DeviceId.From(request.DeviceId), cancellationToken);
        if (device == null || !device.IsActive)
            return Result<int>.Failure("Dispositivo no encontrado o inactivo");

        // Otra sincronización pudo completarlos ya: no se vuelve al reloj por ellos.
        var deviceIds = await ExternalEmployeeDeviceIds.LoadAsync(_branchRepository, cancellationToken);
        var pending = new List<(string DeviceUserId, ExternalEmployee Employee)>();
        foreach (var deviceUserId in request.DeviceUserIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var employee = await deviceIds.FindAsync(_externalEmployeeRepository, deviceUserId, cancellationToken);
            if (employee == null)
                _logger.LogWarning("Empleado externo {DeviceUserId} no encontrado; se omite su sincronización de huellas", deviceUserId);
            else if (IsMissingFingerprints(employee) && pending.All(p => p.Employee.Id != employee.Id))
                pending.Add((deviceIds.DeviceUserIdOf(employee), employee));
        }
        if (pending.Count == 0) return Result<int>.Success(0);

        Result<int> result = Result<int>.Failure("No se pudo usar el dispositivo");
        await _deviceLock.ExecuteWithLockAsync(device.Id.Value, async () =>
            result = await SyncAsync(device, pending, cancellationToken), cancellationToken);
        return result;
    }

    private async Task<Result<int>> SyncAsync(
        Device device, List<(string DeviceUserId, ExternalEmployee Employee)> employees, CancellationToken cancellationToken)
    {
        var deviceClient = _deviceClientFactory.GetClient(device);
        try
        {
            if (!await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken))
                return Result<int>.Failure("No se pudo conectar al dispositivo");

            var updated = 0;
            foreach (var (deviceUserId, employee) in employees)
            {
                try
                {
                    var deviceUser = await deviceClient.GetUserAsync(deviceUserId, cancellationToken);
                    if (deviceUser == null)
                    {
                        _logger.LogWarning("Empleado externo {DeviceUserId} no encontrado en el dispositivo {DeviceId}", deviceUserId, device.Id.Value);
                        continue;
                    }

                    var fingerprints = deviceUser.Fingerprints?.Select(fp => new ExternalEmployeeFingerprint(fp.Index, fp.Template));
                    if (employee.UpdateFromDevice(fingerprints, deviceUser.CardNumber))
                    {
                        _externalEmployeeRepository.Update(employee);
                        updated++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error sincronizando huellas del empleado externo {DeviceUserId} desde dispositivo {DeviceId}", deviceUserId, device.Id.Value);
                }
            }

            if (updated > 0) await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Huellas sincronizadas para {Updated} de {Count} empleados externos desde el dispositivo {DeviceId}", updated, employees.Count, device.Id.Value);
            return Result<int>.Success(updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error sincronizando huellas de externos desde dispositivo {DeviceId}", device.Id.Value);
            return Result<int>.Failure($"Error sincronizando huellas: {ex.Message}");
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
