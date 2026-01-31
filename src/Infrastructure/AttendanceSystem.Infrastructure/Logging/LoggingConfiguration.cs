using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace AttendanceSystem.Infrastructure.Logging;

/// <summary>
/// Configuración centralizada de Serilog para el sistema de asistencia
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Configura Serilog con múltiples sinks (consola, archivo, base de datos)
    /// La configuración se lee desde appsettings.json
    /// </summary>
    public static IServiceCollection AddSerilogLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configurar Serilog desde appsettings.json
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", "AttendanceSystem")
            .CreateLogger();

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddSerilog(dispose: true);
        });

        return services;
    }
}
