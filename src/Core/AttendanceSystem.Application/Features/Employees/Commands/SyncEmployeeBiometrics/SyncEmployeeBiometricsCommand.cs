using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Employees.Commands.SyncEmployeeBiometrics;

public sealed record SyncEmployeeBiometricsCommand(string DeviceId, string EmployeeId) : IRequest<Result<bool>>;

public sealed class SyncEmployeeBiometricsCommandHandler : IRequestHandler<SyncEmployeeBiometricsCommand, Result<bool>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncEmployeeBiometricsCommandHandler> _logger;

    public SyncEmployeeBiometricsCommandHandler(
        IEmployeeRepository employeeRepository,
        IDeviceRepository deviceRepository,
        IDeviceClientFactory deviceClientFactory,
        IUnitOfWork unitOfWork,
        ILogger<SyncEmployeeBiometricsCommandHandler> logger)
    {
        _employeeRepository = employeeRepository;
        _deviceRepository = deviceRepository;
        _deviceClientFactory = deviceClientFactory;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(SyncEmployeeBiometricsCommand request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
        
        if (employee == null)
            return Result<bool>.Failure("Empleado no encontrado");

        var deviceId = DeviceId.From(request.DeviceId);
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        
        if (device == null || !device.IsActive)
            return Result<bool>.Failure("Dispositivo no encontrado o inactivo");

        try
        {
            var deviceClient = _deviceClientFactory.GetClient(device.Brand);
            
            // Connect to device (SDK mode fallback to ADMS command if supported)
            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            
            if (!connected)
                return Result<bool>.Failure("No se pudo conectar al dispositivo");

            var deviceUser = await deviceClient.GetUserAsync(request.EmployeeId, cancellationToken);
            
            if (deviceUser != null)
            {
                var domainFingerprints = deviceUser.Fingerprints?.Select(fp => new EmployeeFingerprint(fp.Index, fp.Template)).ToList();
                
                employee.UpdateBiometrics(
                    cardNumber: deviceUser.CardNumber,
                    devicePassword: string.IsNullOrEmpty(deviceUser.Password) ? null : deviceUser.Password,
                    faceTemplate: deviceUser.FaceTemplate,
                    fingerprints: domainFingerprints,
                    photo: deviceUser.Photo
                );

                _employeeRepository.Update(employee);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Biometría sincronizada exitosamente para el empleado {EmployeeId} desde el dispositivo {DeviceId}", request.EmployeeId, request.DeviceId);
            }
            else
            {
                 _logger.LogWarning("Empleado {EmployeeId} no encontrado en el dispositivo {DeviceId}", request.EmployeeId, request.DeviceId);
            }

            await deviceClient.DisconnectAsync(cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sincronizando biometría para empleado {EmployeeId} desde dispositivo {DeviceId}", request.EmployeeId, request.DeviceId);
            return Result<bool>.Failure($"Error sincronizando biometría: {ex.Message}");
        }
    }
}
