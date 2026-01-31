using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.ImportUsersFromDevice;

public sealed record ImportUsersFromDeviceCommand(string DeviceId) : IRequest<Result<int>>;

public class ImportUsersFromDeviceCommandHandler : IRequestHandler<ImportUsersFromDeviceCommand, Result<int>>
{
    private readonly IZKTecoDeviceClient _zkClient;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ImportUsersFromDeviceCommandHandler> _logger;

    public ImportUsersFromDeviceCommandHandler(
        IZKTecoDeviceClient zkClient,
        IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork,
        ILogger<ImportUsersFromDeviceCommandHandler> logger)
    {
        _zkClient = zkClient;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(ImportUsersFromDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceRepository.GetByIdAsync(DeviceId.From(request.DeviceId), cancellationToken);
            if (device == null)
            {
                return Result<int>.Failure($"Dispositivo {request.DeviceId} no encontrado.");
            }

            if (!await _zkClient.ConnectAsync(device.IpAddress, device.Port, cancellationToken))
            {
                return Result<int>.Failure($"No se pudo conectar al dispositivo {device.Name} ({device.IpAddress}).");
            }

            try
            {
                var deviceUsers = await _zkClient.GetAllUsersAsync(cancellationToken);
                _logger.LogInformation("Se obtuvieron {Count} usuarios del dispositivo. Procesando...", deviceUsers.Count);

                // Cargar todos los empleados en memoria para evitar consultas N+1 y verificar existencia eficientemente.
                // EF Core carga las entidades propiedad (Fingerprints) automáticamente.
                var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
                var employeeDict = allEmployees.ToDictionary(e => e.Id.Value);

                int processedCount = 0;
                int skippedCount = 0;
                
                // Procesar usuarios únicos del dispositivo
                foreach (var dUser in deviceUsers.DistinctBy(u => u.UserId))
                {
                    // Validar ID
                    if (string.IsNullOrWhiteSpace(dUser.UserId)) continue;

                    if (employeeDict.TryGetValue(dUser.UserId, out var employee))
                    {
                        // CASO: Usuario Ya Registrado en BD -> Solo actualizamos métodos de registro (Biometría)
                        // Esto asegura que no tengamos errores de claves duplicadas y mantenemos los datos demográficos de la BD.
                        
                        var fingerprints = dUser.Fingerprints?
                            .Select(fp => new EmployeeFingerprint(fp.Index, fp.Template))
                            .ToList() ?? new List<EmployeeFingerprint>();

                        employee.UpdateBiometrics(
                             dUser.CardNumber, 
                             dUser.Password, 
                             dUser.FaceTemplate, 
                             fingerprints);
                         
                         _employeeRepository.Update(employee);
                         processedCount++;
                    }
                    else
                    {
                        // CASO: Usuario NO existe en BD -> Omitimos creación.
                        // Esto evita errores por "Falta de Datos" (Branch, Department, Position son obligatorios en Dominio).
                        // El usuario debe ser creado primero en el sistema administrativo.
                        _logger.LogWarning("Usuario {Id} encontrado en dispositivo pero NO en base de datos. Se omite para evitar errores de datos incompletos.", dUser.UserId);
                        skippedCount++;
                    }
                }
                
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                
                _logger.LogInformation("Sincronización completada. Actualizados: {Updated}, Omitidos (No en BD): {Skipped}", processedCount, skippedCount);
                
                return Result<int>.Success(processedCount);
            }
            finally
            {
                 await _zkClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error sincronizando usuarios desde dispositivo");
             return Result<int>.Failure($"Error durante la importación: {ex.Message}");
        }
    }
}
