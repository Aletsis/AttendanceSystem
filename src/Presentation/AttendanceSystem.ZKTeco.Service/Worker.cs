using AttendanceSystem.ZKTeco.Adapters;

namespace AttendanceSystem.ZKTeco.Service;

/// <summary>
/// Worker que mantiene el servicio gRPC activo y proporciona monitoreo.
/// Implementa graceful shutdown para detener el servicio y liberar conexiones de manera ordenada.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IZKTecoSessionManager _sessionManager;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IHostApplicationLifetime applicationLifetime,
        IZKTecoSessionManager sessionManager)
    {
        _logger = logger;
        _configuration = configuration;
        _applicationLifetime = applicationLifetime;
        _sessionManager = sessionManager;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("🚀 SERVICIO ZKTECO INICIANDO");
        _logger.LogInformation("========================================");

        // Registrar manejadores para los eventos del ciclo de vida
        _applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        _applicationLifetime.ApplicationStopped.Register(OnApplicationStopped);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var grpcPort = _configuration.GetValue<int>("GrpcPort", 5001);

        _logger.LogInformation("✅ Servicio ZKTeco iniciado correctamente");
        _logger.LogInformation("📡 Servidor gRPC escuchando en puerto: {Port}", grpcPort);
        _logger.LogInformation("⏰ Iniciado en: {Time}", DateTimeOffset.Now);
        _logger.LogInformation("========================================");
        _logger.LogInformation("");

        var healthCheckInterval = TimeSpan.FromMinutes(5);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("💚 Servicio activo - Heartbeat en: {Time}", DateTimeOffset.Now);
                await Task.Delay(healthCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⏹️ Cancelación de servicio solicitada");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en el loop del worker");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("🛑 Servicio ZKTeco finalizando ejecución");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("⚠️ INICIANDO APAGADO ORDENADO DEL SERVICIO ZKTECO");

        try
        {
            _logger.LogInformation("Cerrando y liberando conexiones activas hacia dispositivos ZKTeco...");
            await _sessionManager.CloseAllSessionsAsync(cancellationToken);
            _logger.LogInformation("✅ Conexiones de dispositivos cerradas limpiamente.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⏱️ Timeout alcanzado durante el cierre de sesiones");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error durante el apagado del servicio y liberación de sesiones");
        }

        await base.StopAsync(cancellationToken);
    }

    private void OnApplicationStopping()
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("🔄 Aplicación deteniéndose...");
        _logger.LogInformation("========================================");
    }

    private void OnApplicationStopped()
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("✅ SERVICIO ZKTECO DETENIDO COMPLETAMENTE");
        _logger.LogInformation("⏰ Detenido en: {Time}", DateTimeOffset.Now);
        _logger.LogInformation("========================================");
    }
}
