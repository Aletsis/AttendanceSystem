namespace AttendanceSystem.ZKTeco.Service;

/// <summary>
/// Worker que mantiene el servicio gRPC activo y proporciona monitoreo.
/// Implementa graceful shutdown para detener el servicio de manera ordenada.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public Worker(
        ILogger<Worker> logger, 
        IConfiguration configuration,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _configuration = configuration;
        _applicationLifetime = applicationLifetime;
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

        // El servicio gRPC se configura en Program.cs
        // Este worker proporciona monitoreo y health checks

        var healthCheckInterval = TimeSpan.FromMinutes(5);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("💚 Servicio activo - Health check en: {Time}", DateTimeOffset.Now);
                
                // Aquí podrías agregar health checks adicionales
                // Por ejemplo: verificar conectividad con dispositivos, memoria, etc.
                
                await Task.Delay(healthCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Cancelación normal durante el shutdown
                _logger.LogInformation("⏹️ Cancelación de servicio solicitada");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en el worker del servicio");
                
                // Esperar un poco antes de continuar para evitar loops rápidos en caso de error
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

        _logger.LogInformation("🛑 Servicio ZKTeco finalizando ejecución normal");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("⚠️ INICIANDO APAGADO ORDENADO DEL SERVICIO ZKTECO");
        
        try
        {
            // Dar tiempo para que las operaciones en curso terminen
            var gracePeriod = TimeSpan.FromSeconds(5);
            _logger.LogInformation("⏳ Esperando {Seconds} segundos para operaciones en curso...", gracePeriod.TotalSeconds);
            
            await Task.Delay(gracePeriod, cancellationToken);
            
            _logger.LogInformation("✅ Período de gracia completado");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⏱️ Timeout alcanzado durante el apagado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error durante el apagado del servicio");
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
