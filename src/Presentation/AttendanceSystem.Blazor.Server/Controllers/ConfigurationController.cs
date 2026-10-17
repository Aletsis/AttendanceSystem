using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AttendanceSystem.Blazor.Server.Controllers;

[ApiController]
[Route("api/configuration")]
public class ConfigurationController : ControllerBase
{
    private readonly ISystemConfigurationRepository _repository;
    private readonly IAttendanceJobScheduler _jobScheduler;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(
        ISystemConfigurationRepository repository,
        IAttendanceJobScheduler jobScheduler,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<ConfigurationController> logger)
    {
        _repository = repository;
        _jobScheduler = jobScheduler;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("reload-jobs")]
    public async Task<IActionResult> ReloadJobs()
    {
        // H01: Exigir autorización: Rol Administrador o x-api-key válida
        var isAuthorized = User.IsInRole("Administrador");
        if (!isAuthorized)
        {
            var configuredApiKey = _configuration["ApiKey"] ?? _configuration["ZKTecoService:ApiKey"];
            if (Request.Headers.TryGetValue("x-api-key", out var providedApiKey) &&
                !string.IsNullOrWhiteSpace(configuredApiKey) &&
                string.Equals(providedApiKey.ToString(), configuredApiKey, StringComparison.Ordinal))
            {
                isAuthorized = true;
            }
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Intento no autorizado de recargar tareas programadas.");
            return Unauthorized(new { Success = false, Message = "Acceso no autorizado: Se requiere rol Administrador o x-api-key válida." });
        }

        _logger.LogInformation("Recibida solicitud autorizada de recarga de tareas programadas.");
        try
        {
            var config = await _repository.GetConfigurationAsync(default);
            if (config == null)
            {
                _logger.LogWarning("No se encontró configuración del sistema en la base de datos.");
                return NotFound(new { Success = false, Message = "Configuración no encontrada en la base de datos." });
            }

            // Aplicar descargas automáticas
            if (config.IsAutoDownloadEnabled && config.AutoDownloadTime.HasValue)
            {
                _jobScheduler.ScheduleAutoDownload(config.AutoDownloadTime.Value);
                _logger.LogInformation("Autodescarga programada a las: {Time}", config.AutoDownloadTime.Value);
            }
            else
            {
                _jobScheduler.DisableAutoDownload();
                _logger.LogInformation("Autodescarga deshabilitada.");
            }

            // Aplicar respaldos automáticos
            if (config.IsAutoBackupEnabled && config.AutoBackupTime.HasValue)
            {
                _jobScheduler.ScheduleAutoBackup(config.AutoBackupTime.Value);
                _logger.LogInformation("Autorespaldo programado a las: {Time}", config.AutoBackupTime.Value);
            }
            else
            {
                _jobScheduler.DisableAutoBackup();
                _logger.LogInformation("Autorespaldo deshabilitado.");
            }

            // Aplicar reportes automáticos
            if (config.IsAutoReportEnabled && config.AutoReportTime.HasValue)
            {
                _jobScheduler.ScheduleAutoReport(config.AutoReportTime.Value);
                _logger.LogInformation("Autoreporte programado a las: {Time}", config.AutoReportTime.Value);
            }
            else
            {
                _jobScheduler.DisableAutoReport();
                _logger.LogInformation("Autoreporte deshabilitado.");
            }

            return Ok(new { Success = true, Message = "Tareas en segundo plano actualizadas correctamente." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recargando las tareas programadas.");
            return StatusCode(500, new { Success = false, Message = $"Error interno del servidor: {ex.Message}" });
        }
    }
}
