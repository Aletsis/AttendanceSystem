using AttendanceSystem.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Blazor.Server.Services;

/// <summary>
/// Controla la pausa y reanudación del servidor de Hangfire durante operaciones críticas como restauraciones de base de datos.
/// </summary>
public class HangfireJobControlService : IBackgroundJobControlService
{
    private readonly IEnumerable<IHostedService> _hostedServices;
    private readonly ILogger<HangfireJobControlService> _logger;
    private IHostedService? _hangfireHostedService;

    public HangfireJobControlService(
        IEnumerable<IHostedService> hostedServices,
        ILogger<HangfireJobControlService> logger)
    {
        _hostedServices = hostedServices;
        _logger = logger;
    }

    public async Task PauseBackgroundJobsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _hangfireHostedService ??= _hostedServices.FirstOrDefault(s =>
                s.GetType().FullName?.Contains("Hangfire", StringComparison.OrdinalIgnoreCase) == true);

            if (_hangfireHostedService != null)
            {
                _logger.LogInformation("⏸️ Pausando el servidor de Hangfire para evitar accesos concurrentes a la base de datos...");
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                await _hangfireHostedService.StopAsync(linkedCts.Token);
                _logger.LogInformation("✅ Servidor de Hangfire pausado exitosamente.");
            }
            else
            {
                _logger.LogInformation("ℹ️ No se detectó un servicio hospedado activo de Hangfire.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Advertencia al intentar pausar el servidor de Hangfire.");
        }
    }

    public async Task ResumeBackgroundJobsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _hangfireHostedService ??= _hostedServices.FirstOrDefault(s =>
                s.GetType().FullName?.Contains("Hangfire", StringComparison.OrdinalIgnoreCase) == true);

            if (_hangfireHostedService != null)
            {
                _logger.LogInformation("▶️ Reanudando el servidor de Hangfire tras la operación en la base de datos...");
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                await _hangfireHostedService.StartAsync(linkedCts.Token);
                _logger.LogInformation("✅ Servidor de Hangfire reanudado exitosamente.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Advertencia al intentar reanudar el servidor de Hangfire.");
        }
    }
}
