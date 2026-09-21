using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace AttendanceSystem.Infrastructure.Logging;

/// <summary>
/// Configuración centralizada de Serilog para el sistema de asistencia.
/// Separa los logs en múltiples archivos y carpetas según el dominio funcional:
/// - ADMS (logs/adms/adms-.log)
/// - Base de Datos (logs/database/database-.log)
/// - Procesamiento de Asistencia (logs/attendance/attendance-processing-.log)
/// - Comunicación ZKTeco (logs/zkteco/zkteco-comm-.log)
/// - Sistema / General (logs/system/system-.log)
/// - Errores Globales (logs/errors/errors-.log)
/// </summary>
public static class LoggingConfiguration
{
    private const string DefaultOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Configura Serilog con sub-loggers especializados por funcionalidad,
    /// enriquecedores y sinks para archivo, consola y base de datos.
    /// </summary>
    public static LoggerConfiguration ConfigureAttendanceLogging(
        this LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        IServiceProvider? services = null)
    {
        // 1. Lectura de configuración base (appsettings.json)
        loggerConfiguration.ReadFrom.Configuration(configuration);

        if (services != null)
        {
            loggerConfiguration.ReadFrom.Services(services);
        }

        // 2. Enriquecedores globales
        loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", "AttendanceSystem");

        // 3. Sub-logger: ADMS (Controlador, servicios y clientes ADMS)
        loggerConfiguration.WriteTo.Logger(admsLogger => admsLogger
            .Filter.ByIncludingOnly(e => IsMatchingContext(e,
                "AttendanceSystem.Blazor.Server.Controllers.Adms",
                "AttendanceSystem.Infrastructure.Services.Adms",
                "AttendanceSystem.Infrastructure.Adapters.Adms"))
            .WriteTo.File(
                path: "logs/adms/adms-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10485760, // 10 MB
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate));

        // 4. Sub-logger: Base de Datos (EF Core, Npgsql, Repositorios y Migraciones)
        loggerConfiguration.WriteTo.Logger(dbLogger => dbLogger
            .Filter.ByIncludingOnly(e => IsMatchingContext(e,
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "AttendanceSystem.Infrastructure.Persistence"))
            .WriteTo.File(
                path: "logs/database/database-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 15,
                fileSizeLimitBytes: 20971520, // 20 MB
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate));

        // 5. Sub-logger: Procesamiento de Asistencia (Cálculos, Turnos, Deduplicación, Jobs de Asistencia)
        loggerConfiguration.WriteTo.Logger(attLogger => attLogger
            .Filter.ByIncludingOnly(e => IsMatchingContext(e,
                "AttendanceSystem.Application.Features.Attendance",
                "AttendanceSystem.Domain.Services.Attendance",
                "AttendanceSystem.Infrastructure.Services.Attendance"))
            .WriteTo.File(
                path: "logs/attendance/attendance-processing-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 60,
                fileSizeLimitBytes: 10485760, // 10 MB
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate));

        // 6. Sub-logger: Comunicación ZKTeco (gRPC, Dispositivos ZKTeco, Discovery)
        loggerConfiguration.WriteTo.Logger(zkLogger => zkLogger
            .Filter.ByIncludingOnly(e => IsMatchingContext(e,
                "AttendanceSystem.Infrastructure.Adapters.GrpcZKTeco",
                "AttendanceSystem.Infrastructure.Adapters.DeviceClientFactory",
                "AttendanceSystem.ZKTeco"))
            .WriteTo.File(
                path: "logs/zkteco/zkteco-comm-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10485760, // 10 MB
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate));

        // 7. Sub-logger: Sistema / General (Hosting, ASP.NET Core, Hangfire general, Auth, etc.)
        loggerConfiguration.WriteTo.Logger(sysLogger => sysLogger
            .Filter.ByIncludingOnly(e => !IsDomainSpecificContext(e))
            .WriteTo.File(
                path: "logs/system/system-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10485760, // 10 MB
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate));

        // 8. Sub-logger: Errores Globales (Error y Fatal de cualquier origen)
        loggerConfiguration.WriteTo.Logger(errLogger => errLogger
            .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
            .WriteTo.File(
                path: "logs/errors/errors-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                fileSizeLimitBytes: 10485760, // 10 MB
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate));

        return loggerConfiguration;
    }

    /// <summary>
    /// Método de compatibilidad para registro en IServiceCollection
    /// </summary>
    public static IServiceCollection AddSerilogLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ConfigureAttendanceLogging(configuration)
            .CreateLogger();

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddSerilog(dispose: true);
        });

        return services;
    }

    private static bool IsMatchingContext(LogEvent e, params string[] prefixes)
    {
        if (e.Properties.TryGetValue("SourceContext", out var sourceContextValue) &&
            sourceContextValue is ScalarValue { Value: string sourceContext })
        {
            foreach (var prefix in prefixes)
            {
                if (sourceContext.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    sourceContext.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsDomainSpecificContext(LogEvent e)
    {
        return IsMatchingContext(e,
            "AttendanceSystem.Blazor.Server.Controllers.Adms",
            "AttendanceSystem.Infrastructure.Services.Adms",
            "AttendanceSystem.Infrastructure.Adapters.Adms",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "AttendanceSystem.Infrastructure.Persistence",
            "AttendanceSystem.Application.Features.Attendance",
            "AttendanceSystem.Domain.Services.Attendance",
            "AttendanceSystem.Infrastructure.Services.Attendance",
            "AttendanceSystem.Infrastructure.Adapters.GrpcZKTeco",
            "AttendanceSystem.Infrastructure.Adapters.DeviceClientFactory",
            "AttendanceSystem.ZKTeco");
    }
}
