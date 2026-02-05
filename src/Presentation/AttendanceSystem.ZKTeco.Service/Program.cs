using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using AttendanceSystem.ZKTeco.Service.Services;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.ZKTeco.Adapters;
using Serilog;
using Serilog.Events;

namespace AttendanceSystem.ZKTeco.Service;

public class Program
{
    public static void Main(string[] args)
    {
        // ===== BOOTSTRAP LOGGER =====
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/zkteco-service-log-.txt", rollingInterval: RollingInterval.Day)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Iniciando servicio ZKTeco...");

            var builder = WebApplication.CreateBuilder(args);

            // ===== CONFIGURACIÓN DE GRACEFUL SHUTDOWN =====
            var shutdownTimeoutSeconds = builder.Configuration.GetValue<int>("ShutdownTimeoutSeconds", 30);
            builder.Host.ConfigureHostOptions(options =>
            {
                options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeoutSeconds);
            });

            // ===== LOGGING CON SERILOG =====
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "ZKTecoService"));


        // ===== CONFIGURAR COMO SERVICIO DE WINDOWS =====
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "AttendanceSystem.ZKTeco.Service";
        });

        // Agregar el worker (necesario para que funcione como servicio de Windows)
        builder.Services.AddHostedService<Worker>();

        // Configurar gRPC Server
        builder.Services.AddGrpc();
        
        // ZKTeco SDK Service registration
        builder.Services.AddSingleton<IZKTecoDeviceClient, ZKTecoDeviceClient>(); 

        // Configurar Kestrel explícitamente si es necesario, o usar appsettings
        builder.WebHost.ConfigureKestrel(options =>
        {
            var port = builder.Configuration.GetValue<int>("GrpcPort", 5001);
            options.ListenAnyIP(port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        var app = builder.Build();

        app.MapGrpcService<ZKTecoGrpcService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

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
