using AttendanceSystem.Adms;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DependencyInjection;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Infrastructure.DependencyInjection;
using AttendanceSystem.Infrastructure.Logging;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AttendanceSystem.WPF.Services;

/// <summary>
/// El backend que corre dentro del cliente WPF. Ejecuta lo mismo que el sitio web en segundo plano (tareas de Hangfire,
/// consolidación y outbox) para no depender de que el sitio esté encendido; con el sitio instalado comparten la base y
/// el almacenamiento de Hangfire, y cada trabajo lo toma un solo proceso.
/// Sin sitio web (<c>Hosting:Standalone</c>) además administra el esquema y recibe a los relojes ADMS.
/// </summary>
public static class WpfBackendHost
{
    public const string StandaloneKey = "Hosting:Standalone";
    public const int DefaultAdmsPort = 18373;

    public static IHost Build(bool standalone)
    {
        if (!standalone)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            ConfigureLogging(builder.Logging, builder.Configuration);
            AddBackend(builder.Services, builder.Configuration, standalone);
            return builder.Build();
        }

        var web = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        ConfigureLogging(web.Logging, web.Configuration);
        AddBackend(web.Services, web.Configuration, standalone);
        AddAdmsReceiver(web.Services);

        var app = web.Build();
        app.UseMiddleware<AdmsRequestLoggingMiddleware>();
        app.UseRouting();
        // 503 a los relojes durante una restauración, igual que en el sitio web.
        app.UseMiddleware<RestoreMaintenanceMiddleware>();
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }

    private static void ConfigureLogging(ILoggingBuilder logging, IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();
        logging.ClearProviders();
        logging.AddSerilog();
    }

    private static void AddBackend(IServiceCollection services, IConfiguration configuration, bool standalone)
    {
        // Registrar servicios compartidos de Aplicación e Infraestructura (H13 & H15)
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);

        // Servicios específicos de cliente WPF
        services.AddSingleton<IAuthenticationStateService, AuthenticationStateService>();
        services.AddScoped<WpfOperationScope>();
        services.AddScoped<ICurrentUserService, WpfCurrentUserService>();

        // Hangfire: el mismo almacenamiento que el sitio web. Sólo sin sitio web se crea su esquema (como las migraciones).
        var hangfireConnection = configuration.GetConnectionString("HangfireDb") ?? configuration.GetConnectionString("AttendanceDb");
        services.AddHangfire(config => config
            .UsePostgreSqlStorage(
                c => c.UseNpgsqlConnection(hangfireConnection),
                new PostgreSqlStorageOptions { UseSlidingInvisibilityTimeout = true, PrepareSchemaIfNecessary = standalone }));
        // Servidor de Hangfire con pausa y reanudación para las restauraciones, como en el sitio web.
        services.AddSingleton<HangfireJobControlService>();
        services.AddSingleton<IBackgroundJobControlService>(sp => sp.GetRequiredService<HangfireJobControlService>());
        services.AddHostedService(sp => sp.GetRequiredService<HangfireJobControlService>());
        services.AddScoped<IAttendanceJobScheduler, HangfireAttendanceJobScheduler>();
        services.AddScoped<AttendanceJobs>();

        // Add Identity Core (UserManager only — sin cookies, SignInManager ni HTTP context)
        // Necesario para validar contraseñas contra la misma tabla AspNetUsers que usa Blazor
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            // Misma política que la versión web: al cambiar la contraseña desde aquí no debe aceptarse una más débil.
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;

            // Configuración de bloqueo compartida con el servidor web (H14)
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AttendanceDbContext>();
    }

    /// <summary>
    /// Endpoints ADMS en el puerto configurado en el sistema (el mismo campo que usa el sitio web). El puerto se lee al
    /// iniciar el receptor, después de dejar la base lista.
    /// </summary>
    private static void AddAdmsReceiver(IServiceCollection services)
    {
        services.AddControllers().AddAdmsReceiver();
        services.AddAuthorization();
        services.AddOptions<KestrelServerOptions>().Configure<IServiceScopeFactory, ILoggerFactory>((options, scopes, loggers) =>
        {
            var port = DefaultAdmsPort;
            try
            {
                using var scope = scopes.CreateScope();
                port = scope.ServiceProvider.GetRequiredService<ISystemConfigurationRepository>()
                    .GetConfigurationAsync().GetAwaiter().GetResult()?.AdmsPort ?? DefaultAdmsPort;
            }
            catch (Exception ex)
            {
                loggers.CreateLogger(typeof(WpfBackendHost)).LogWarning(ex, "No se pudo leer el puerto ADMS configurado; se usa {Port}.", port);
            }
            options.ListenAnyIP(port);
        });
    }
}
