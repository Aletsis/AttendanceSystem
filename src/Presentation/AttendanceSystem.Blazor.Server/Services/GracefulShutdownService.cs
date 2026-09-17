using Hangfire;
using Microsoft.EntityFrameworkCore;
using AttendanceSystem.Infrastructure.Persistence;

namespace AttendanceSystem.Blazor.Server.Services;

/// <summary>
/// Servicio que gestiona el apagado ordenado de la aplicación.
/// Asegura que todos los recursos se liberen correctamente y que las operaciones en curso se completen.
/// </summary>
public class GracefulShutdownService : IHostedService
{
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private CancellationTokenSource? _shutdownCts;

    public GracefulShutdownService(
        ILogger<GracefulShutdownService> logger,
        IHostApplicationLifetime applicationLifetime,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛡️ Servicio de Graceful Shutdown iniciado");

        // Registrar manejadores para los eventos del ciclo de vida de la aplicación
        _applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        _applicationLifetime.ApplicationStopped.Register(OnApplicationStopped);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛡️ Servicio de Graceful Shutdown detenido");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Se ejecuta cuando la aplicación comienza a detenerse.
    /// Aquí se realizan las operaciones de limpieza ordenada.
    /// </summary>
    private void OnApplicationStopping()
    {
        _logger.LogWarning("⚠️ INICIANDO APAGADO ORDENADO DE LA APLICACIÓN");
        _logger.LogInformation("========================================");

        // Obtener el timeout de apagado desde la configuración (por defecto 30 segundos)
        var shutdownTimeoutSeconds = _configuration.GetValue<int>("ShutdownTimeoutSeconds", 30);
        _shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(shutdownTimeoutSeconds));

        try
        {
            // Ejecutar el apagado ordenado de forma sincrónica
            ShutdownGracefullyAsync(_shutdownCts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error durante el apagado ordenado");
        }
    }

    /// <summary>
    /// Se ejecuta cuando la aplicación se ha detenido completamente.
    /// </summary>
    private void OnApplicationStopped()
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("✅ APLICACIÓN DETENIDA COMPLETAMENTE");
        _logger.LogInformation("========================================");

        _shutdownCts?.Dispose();
    }

    /// <summary>
    /// Realiza el apagado ordenado de todos los componentes del sistema.
    /// </summary>
    private async Task ShutdownGracefullyAsync(CancellationToken cancellationToken)
    {
        var shutdownSteps = new List<(string Name, Func<CancellationToken, Task> Action)>
        {
            ("Detener aceptación de nuevas solicitudes HTTP", StopAcceptingNewRequestsAsync),
            ("Esperar finalización de trabajos de Hangfire", WaitForHangfireJobsAsync),
            ("Cerrar conexiones activas de base de datos", CloseActiveDatabaseConnectionsAsync),
            ("Liberar recursos de servicios singleton", ReleaseServiceResourcesAsync),
            ("Flush de logs pendientes", FlushLogsAsync)
        };

        for (int i = 0; i < shutdownSteps.Count; i++)
        {
            var (name, action) = shutdownSteps[i];

            try
            {
                _logger.LogInformation("📋 Paso {Step}/{Total}: {Name}...", i + 1, shutdownSteps.Count, name);
                await action(cancellationToken);
                _logger.LogInformation("✅ Completado: {Name}", name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("⏱️ Timeout alcanzado en: {Name}", name);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en: {Name}", name);
                // Continuar con los siguientes pasos incluso si uno falla
            }
        }
    }

    /// <summary>
    /// Paso 1: Detener la aceptación de nuevas solicitudes HTTP.
    /// </summary>
    private Task StopAcceptingNewRequestsAsync(CancellationToken cancellationToken)
    {
        // En ASP.NET Core, esto se maneja automáticamente cuando se inicia el shutdown
        // Aquí podríamos agregar lógica adicional si fuera necesario
        _logger.LogInformation("   ℹ️ El servidor dejará de aceptar nuevas conexiones");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Paso 2: Esperar a que los trabajos de Hangfire en ejecución terminen.
    /// </summary>
    private async Task WaitForHangfireJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();
            if (monitoringApi == null)
            {
                _logger.LogInformation("   ℹ️ Hangfire no está disponible o no hay trabajos en ejecución");
                return;
            }

            var maxWaitTime = TimeSpan.FromSeconds(20);
            var checkInterval = TimeSpan.FromSeconds(1);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWaitTime && !cancellationToken.IsCancellationRequested)
            {
                var processingJobs = monitoringApi.ProcessingJobs(0, int.MaxValue);

                if (processingJobs.Count == 0)
                {
                    _logger.LogInformation("   ✅ No hay trabajos de Hangfire en ejecución");
                    break;
                }

                _logger.LogInformation("   ⏳ Esperando {Count} trabajo(s) de Hangfire...", processingJobs.Count);
                await Task.Delay(checkInterval, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "   ⚠️ Error al verificar trabajos de Hangfire");
        }
    }

    /// <summary>
    /// Paso 3: Cerrar conexiones activas de base de datos.
    /// </summary>
    private async Task CloseActiveDatabaseConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetService<AttendanceDbContext>();

            if (dbContext != null)
            {
                _logger.LogInformation("   🔌 Cerrando conexiones de base de datos...");
                await dbContext.DisposeAsync();
                _logger.LogInformation("   ✅ Conexiones de base de datos cerradas");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "   ⚠️ Error al cerrar conexiones de base de datos");
        }
    }

    /// <summary>
    /// Paso 4: Liberar recursos de servicios singleton.
    /// </summary>
    private Task ReleaseServiceResourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Aquí podríamos liberar recursos de servicios singleton específicos
            // Por ejemplo, cerrar conexiones de caché, liberar locks, etc.
            _logger.LogInformation("   🧹 Liberando recursos de servicios...");

            // Ejemplo: Si tuviéramos un servicio de caché o conexiones persistentes
            // var cacheService = _serviceProvider.GetService<ICacheService>();
            // await cacheService?.FlushAsync(cancellationToken);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "   ⚠️ Error al liberar recursos de servicios");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Paso 5: Asegurar que todos los logs pendientes se escriban.
    /// </summary>
    private Task FlushLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("   📝 Escribiendo logs pendientes...");
            Serilog.Log.CloseAndFlush();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "   ⚠️ Error al escribir logs pendientes");
            return Task.CompletedTask;
        }
    }
}
