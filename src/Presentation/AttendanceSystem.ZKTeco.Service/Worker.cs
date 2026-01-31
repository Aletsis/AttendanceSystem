namespace AttendanceSystem.ZKTeco.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Servicio ZKTeco iniciado en: {Time}", DateTimeOffset.Now);

        // El servicio gRPC se configura en Program.cs
        // Este worker puede usarse para tareas adicionales de monitoreo

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Servicio activo en: {Time}", DateTimeOffset.Now);
                
                // Aquí podrías agregar health checks, monitoreo, etc.
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el worker del servicio");
            }
        }

        _logger.LogInformation("Servicio ZKTeco detenido en: {Time}", DateTimeOffset.Now);
    }
}
