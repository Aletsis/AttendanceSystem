using AttendanceSystem.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Implementación Singleton y Thread-Safe de IRestoreStateService.
/// </summary>
public class RestoreStateService : IRestoreStateService
{
    private readonly ILogger<RestoreStateService> _logger;
    private volatile bool _isRestoreInProgress;

    public RestoreStateService(ILogger<RestoreStateService> logger)
    {
        _logger = logger;
    }

    public bool IsRestoreInProgress => _isRestoreInProgress;

    public void EnterRestoreMode()
    {
        _isRestoreInProgress = true;
        _logger.LogWarning("🚧 Modo de restauración activado: El sistema ha entrado en modo mantenimiento.");
    }

    public void ExitRestoreMode()
    {
        _isRestoreInProgress = false;
        _logger.LogInformation("✅ Modo de restauración desactivado: El sistema ha vuelto a su operación normal.");
    }
}
