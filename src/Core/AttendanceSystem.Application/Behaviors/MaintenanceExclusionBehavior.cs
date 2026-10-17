using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Behaviors;

/// <summary>
/// Pipeline behavior de MediatR que aplica exclusión global de mantenimiento y restauración.
/// Registra operaciones de escritura activas para drenaje y rechaza nuevos comandos mutantes
/// cuando la base de datos se encuentra en mantenimiento.
/// </summary>
public class MaintenanceExclusionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IRestoreStateService _restoreStateService;
    private readonly ILogger<MaintenanceExclusionBehavior<TRequest, TResponse>> _logger;

    public MaintenanceExclusionBehavior(
        IRestoreStateService restoreStateService,
        ILogger<MaintenanceExclusionBehavior<TRequest, TResponse>> logger)
    {
        _restoreStateService = restoreStateService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        // Operaciones de gestión de respaldo y restauración propias están exentas
        if (requestName.StartsWith("RestoreBackup", StringComparison.OrdinalIgnoreCase) ||
            requestName.StartsWith("GetBackup", StringComparison.OrdinalIgnoreCase))
        {
            return await next();
        }

        var isCommand = requestName.EndsWith("Command", StringComparison.OrdinalIgnoreCase);

        if (isCommand)
        {
            // Rechazar si no se permiten escrituras
            if (!_restoreStateService.CanWrite)
            {
                _logger.LogWarning(
                    "Operación {RequestName} rechazada: Base de datos en mantenimiento ({State}) por {Owner}.",
                    requestName,
                    _restoreStateService.CurrentState,
                    _restoreStateService.CurrentOwner ?? "desconocido");

                throw new DatabaseMaintenanceException(
                    _restoreStateService.CurrentState,
                    _restoreStateService.CurrentOwner,
                    $"No se puede procesar '{requestName}' porque la base de datos está en modo de mantenimiento ({_restoreStateService.CurrentState}).");
            }

            // Registrar escritor activo para permitir drenaje coordinado
            using var writerSlot = _restoreStateService.RegisterActiveWriter();
            return await next();
        }

        // Consultas (Queries): Rechazar solo si la restauración física está en curso
        if (_restoreStateService.CurrentState == MaintenanceState.Restoring ||
            _restoreStateService.CurrentState == MaintenanceState.Recovering)
        {
            _logger.LogWarning(
                "Consulta {RequestName} rechazada: Base de datos en restauración física activa ({State}).",
                requestName,
                _restoreStateService.CurrentState);

            throw new DatabaseMaintenanceException(
                _restoreStateService.CurrentState,
                _restoreStateService.CurrentOwner,
                $"No se puede ejecutar '{requestName}' mientras la base de datos está siendo restaurada físicamente.");
        }

        return await next();
    }
}
