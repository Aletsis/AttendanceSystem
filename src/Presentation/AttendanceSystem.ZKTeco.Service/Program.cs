using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using AttendanceSystem.ZKTeco.Service.Services;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.ZKTeco.Adapters;
using Serilog;
using Serilog.Events;
using AttendanceSystem.ZKTeco.Service.Interceptors;

namespace AttendanceSystem.ZKTeco.Service;

public class Program
{
    public static void Main(string[] args)
    {
        // Garantizar que el directorio actual de trabajo sea el del ejecutable (crítico para Servicios de Windows)
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        // Registrar proveedores de codificación para soportar ANSI/CodePages (necesario para ZKTeco SDK en .NET Core)
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var logsFolder = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(logsFolder);
        }
        catch
        {
            // Si falla la creación inicial, Serilog lo intentará o reportará en SelfLog
        }

        // Habilitar diagnóstico interno de Serilog con control de tamaño máximo (5 MB)
        var internalLogPath = Path.Combine(logsFolder, "serilog-internal.log");
        Serilog.Debugging.SelfLog.Enable(msg =>
        {
            try
            {
                var fileInfo = new FileInfo(internalLogPath);
                if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024)
                {
                    var backupPath = Path.Combine(logsFolder, "serilog-internal.old.log");
                    File.Copy(internalLogPath, backupPath, overwrite: true);
                    File.Delete(internalLogPath);
                }
                File.AppendAllText(internalLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
            }
            catch
            {
                // ignored
            }
        });

        // ===== BOOTSTRAP LOGGER =====
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(logsFolder, "bootstrap", "zkteco-service-bootstrap-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Iniciando servicio ZKTeco desde: {BaseDirectory}", AppContext.BaseDirectory);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });

            // ===== CONFIGURACIÓN DE GRACEFUL SHUTDOWN =====
            var shutdownTimeoutSeconds = builder.Configuration.GetValue<int>("ShutdownTimeoutSeconds", 15);
            builder.Host.ConfigureHostOptions(options =>
            {
                options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeoutSeconds);
            });

            // ===== CONFIGURAR COMO SERVICIO DE WINDOWS =====
            builder.Host.UseWindowsService(options =>
            {
                options.ServiceName = "AttendanceSystem.ZKTeco.Service";
            });

            // ===== LOGGING CON SERILOG =====
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "ZKTecoService")
                .WriteTo.Logger(zkLogger => zkLogger
                    .WriteTo.File(
                        path: Path.Combine(logsFolder, "zkteco", "zkteco-service-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        fileSizeLimitBytes: 10485760,
                        rollOnFileSizeLimit: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"))
                .WriteTo.Logger(errLogger => errLogger
                    .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
                    .WriteTo.File(
                        path: Path.Combine(logsFolder, "errors", "zkteco-errors-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 90,
                        fileSizeLimitBytes: 10485760,
                        rollOnFileSizeLimit: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")));

            // Agregar el worker para monitoreo y tareas de ciclo de vida
            builder.Services.AddHostedService<Worker>();

            // Configurar gRPC Server con límites de mensaje ampliados (32 MB) e interceptor de autenticación
            builder.Services.AddGrpc(options =>
            {
                options.MaxReceiveMessageSize = 32 * 1024 * 1024; // 32 MB
                options.MaxSendMessageSize = 32 * 1024 * 1024;    // 32 MB
                options.EnableDetailedErrors = builder.Environment.IsDevelopment();
                options.Interceptors.Add<ApiKeyAuthInterceptor>();
            });

            // Servicio de salud gRPC estándar (grpc.health.v1.Health)
            builder.Services.AddGrpcHealthChecks();

            // Registro de gestor de sesiones ZKTeco (conexiones bajo demanda con aislamiento por reloj)
            builder.Services.AddSingleton<IZKTecoSessionManager, ZKTecoSessionManager>();
            builder.Services.AddSingleton<IDeviceDiscoveryService, ZKTecoDiscoveryService>();

            // Configurar Kestrel para gRPC en HTTP/2 sin TLS (h2c)
            builder.WebHost.ConfigureKestrel(options =>
            {
                var port = builder.Configuration.GetValue<int>("GrpcPort", 5001);
                options.ListenAnyIP(port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            });

            var app = builder.Build();

            // Mapeo de endpoints gRPC y salud
            app.MapGrpcService<ZKTecoGrpcService>();
            app.MapGrpcHealthChecksService();

            Log.Information("Servicio ZKTeco configurado correctamente");
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "El servicio ZKTeco terminó inesperadamente");
            throw;
        }
        finally
        {
            Log.Information("Cerrando servicio ZKTeco...");
            Log.CloseAndFlush();
        }
    }
}
