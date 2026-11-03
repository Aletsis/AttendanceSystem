using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Employees.Commands.SyncEmployeeBiometrics;

/// <summary>
/// Copia del reloj la biometría (huellas, rostro, tarjeta, contraseña) de los empleados que no la tienen. Una sola
/// conexión por reloj para todos ellos, con el reloj apartado (no compite con una descarga). Falla sólo si no se pudo
/// usar el reloj; un empleado que falla no detiene a los demás. Devuelve cuántos empleados se actualizaron.
/// </summary>
[RequiresPermission(Permissions.EmployeesSendToDevice)]
public sealed record SyncEmployeeBiometricsCommand(string DeviceId, IReadOnlyList<string> EmployeeIds) : IRequest<Result<int>>;

public sealed class SyncEmployeeBiometricsCommandHandler : IRequestHandler<SyncEmployeeBiometricsCommand, Result<int>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceLockService _deviceLock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncEmployeeBiometricsCommandHandler> _logger;

    public SyncEmployeeBiometricsCommandHandler(
        IEmployeeRepository employeeRepository,
        IDeviceRepository deviceRepository,
        IDeviceClientFactory deviceClientFactory,
        IDeviceLockService deviceLock,
        IUnitOfWork unitOfWork,
        ILogger<SyncEmployeeBiometricsCommandHandler> logger)
    {
        _employeeRepository = employeeRepository;
        _deviceRepository = deviceRepository;
        _deviceClientFactory = deviceClientFactory;
        _deviceLock = deviceLock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Sin ninguna biometría: lo único que este comando viene a completar.</summary>
    public static bool IsMissingBiometrics(Employee employee) =>
        !employee.Fingerprints.Any() &&
        string.IsNullOrEmpty(employee.DevicePassword) &&
        string.IsNullOrEmpty(employee.CardNumber) &&
        string.IsNullOrEmpty(employee.FaceTemplate);

    public async Task<Result<int>> Handle(SyncEmployeeBiometricsCommand request, CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(DeviceId.From(request.DeviceId), cancellationToken);
        if (device == null || !device.IsActive)
            return Result<int>.Failure("Dispositivo no encontrado o inactivo");

        // Otra sincronización (o la que encoló una descarga anterior) pudo completarlos ya: no se vuelve al reloj por ellos.
        var pending = new List<Employee>();
        foreach (var id in request.EmployeeIds.Distinct())
        {
            var employee = await _employeeRepository.GetByIdAsync(EmployeeId.From(id), cancellationToken);
            if (employee == null)
                _logger.LogWarning("Empleado {EmployeeId} no encontrado; se omite su sincronización de biometría", id);
            else if (IsMissingBiometrics(employee))
                pending.Add(employee);
        }
        if (pending.Count == 0) return Result<int>.Success(0);

        Result<int> result = Result<int>.Failure("No se pudo usar el dispositivo");
        await _deviceLock.ExecuteWithLockAsync(device.Id.Value, async () =>
            result = await SyncAsync(device, pending, cancellationToken), cancellationToken);
        return result;
    }

    private async Task<Result<int>> SyncAsync(Device device, List<Employee> employees, CancellationToken cancellationToken)
    {
        // El cliente según el método del reloj: a uno ADMS no se le habla por SDK directamente.
        var deviceClient = _deviceClientFactory.GetClient(device);
        try
        {
            if (!await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken))
                return Result<int>.Failure("No se pudo conectar al dispositivo");

            var updated = 0;
            foreach (var employee in employees)
            {
                try
                {
                    var deviceUser = await deviceClient.GetUserAsync(employee.Id.Value, cancellationToken);
                    if (deviceUser == null)
                    {
                        _logger.LogWarning("Empleado {EmployeeId} no encontrado en el dispositivo {DeviceId}", employee.Id.Value, device.Id.Value);
                        continue;
                    }

                    employee.UpdateBiometrics(
                        cardNumber: deviceUser.CardNumber,
                        devicePassword: string.IsNullOrEmpty(deviceUser.Password) ? null : deviceUser.Password,
                        faceTemplate: deviceUser.FaceTemplate,
                        fingerprints: deviceUser.Fingerprints?.Select(fp => new EmployeeFingerprint(fp.Index, fp.Template)).ToList(),
                        photo: deviceUser.Photo);
                    _employeeRepository.Update(employee);
                    updated++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error sincronizando biometría para empleado {EmployeeId} desde dispositivo {DeviceId}", employee.Id.Value, device.Id.Value);
                }
            }

            if (updated > 0) await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Biometría sincronizada para {Updated} de {Count} empleados desde el dispositivo {DeviceId}", updated, employees.Count, device.Id.Value);
            return Result<int>.Success(updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error sincronizando biometría desde dispositivo {DeviceId}", device.Id.Value);
            return Result<int>.Failure($"Error sincronizando biometría: {ex.Message}");
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
