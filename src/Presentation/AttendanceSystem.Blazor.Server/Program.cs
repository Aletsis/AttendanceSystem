using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using MediatR;
using Hangfire;
using Hangfire.PostgreSql;
using MudBlazor.Services;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Adapters;
using AttendanceSystem.Infrastructure.Services;
using AttendanceSystem.Blazor.Server.Services;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.ZKTeco.Grpc;
using QuestPDF.Infrastructure;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Infrastructure.Persistence.Queries;
using AttendanceSystem.Application.Features.Shifts.Queries;
using AttendanceSystem.Application.Features.Branches.Queries;
using AttendanceSystem.Application.Features.Departments.Queries;
using AttendanceSystem.Application.Features.Positions.Queries;
using AttendanceSystem.Infrastructure.Logging;
using Serilog;
using Serilog.Events;


QuestPDF.Settings.License = LicenseType.Community;

// ===== BOOTSTRAP LOGGER =====
// Configurar un logger inicial para capturar errores durante el inicio de la aplicación
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/bootstrap-log-.txt", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    Log.Information("Iniciando configuración del host...");

// ===== NPGSQL DATETIME CONFIGURATION =====
// Configurar Npgsql para convertir DateTime a UTC automáticamente
// Esto es necesario porque PostgreSQL requiere UTC para 'timestamp with time zone'
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);

var builder = WebApplication.CreateBuilder(args);

// ===== CONFIGURACIÓN DE GRACEFUL SHUTDOWN =====
// Configurar el timeout de apagado (por defecto 30 segundos)
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
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "AttendanceSystem"));


// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddControllersWithViews(); // Enable Controllers with Views for Antiforgery support
builder.Services.AddScoped<IReportExportService, ReportExportService>();
builder.Services.AddScoped<IImportService, ImportService>();

// ===== GRACEFUL SHUTDOWN SERVICE =====
builder.Services.AddHostedService<GracefulShutdownService>();

// ===== DOMAIN LAYER =====
// Servicios de dominio
builder.Services.AddScoped<AttendanceDeduplicationService>();

// ===== APPLICATION LAYER =====
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(RecordAttendanceCommand).Assembly);
    // Agregar behavior de logging para todas las solicitudes
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});

builder.Services.AddScoped<AttendanceValidationService>();

// ===== INFRASTRUCTURE LAYER =====
builder.Services.AddDbContext<AttendanceDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AttendanceDb"),
        b => b.MigrationsAssembly("AttendanceSystem.Infrastructure")));

builder.Services.AddDbContextFactory<AttendanceDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AttendanceDb"),
        b => b.MigrationsAssembly("AttendanceSystem.Infrastructure")),
    ServiceLifetime.Scoped);

builder.Services.AddScoped<IUnitOfWork>(sp => 
    sp.GetRequiredService<AttendanceDbContext>());

// Repositorios
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();

// gRPC Client al servicio Windows
builder.Services.AddGrpcClient<ZKTecoService.ZKTecoServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["ZKTecoService:Url"]!);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    // Permitir HTTP/2 sin TLS para conexiones locales
    handler.ServerCertificateCustomValidationCallback = 
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
})
.ConfigureChannel(options =>
{
    // Configurar para usar HTTP en lugar de HTTPS
    options.UnsafeUseInsecureChannelCallCredentials = true;
    options.MaxReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
    options.MaxSendMessageSize = 10 * 1024 * 1024; // 10 MB
});

// Adaptador gRPC que implementa IZKTecoDeviceClient (stub por ahora)
builder.Services.AddScoped<IZKTecoDeviceClient, GrpcZKTecoDeviceClient>();

// Servicios de infraestructura
builder.Services.AddScoped<IEmailService, SendGridEmailService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddSingleton<IDeviceLockService, DeviceLockService>();
builder.Services.AddScoped<IBackupService, BackupService>();

// Servicios de Query (CQRS - Lectura)
builder.Services.AddScoped<IDeviceQueries, DeviceQueries>();
builder.Services.AddScoped<IShiftQueries, ShiftQueries>();
builder.Services.AddScoped<IBranchQueries, BranchQueries>();
builder.Services.AddScoped<IDepartmentQueries, DepartmentQueries>();
builder.Services.AddScoped<IPositionQueries, PositionQueries>();
builder.Services.AddSingleton<IAdmsCommandService, AdmsCommandService>();

// Repositorios
builder.Services.AddScoped<IShiftRepository, ShiftRepository>();
builder.Services.AddScoped<IBranchRepository, BranchRepository>();
builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<IPositionRepository, PositionRepository>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IDailyAttendanceRepository, DailyAttendanceRepository>();
builder.Services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
builder.Services.AddScoped<IDownloadLogRepository, DownloadLogRepository>();

// ===== IDENTITY & AUTHENTICATION =====
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Configuración de contraseña
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    
    // Configuración de usuario
    options.User.RequireUniqueEmail = false;
    
    // Configuración de bloqueo
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AttendanceDbContext>()
.AddDefaultTokenProviders();

// Configurar cookies de autenticación
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/AccessDenied";
});

// Servicios de autenticación para Blazor
builder.Services.AddCascadingAuthenticationState();
// builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// ===== HANGFIRE =====
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(c => 
        c.UseNpgsqlConnection(builder.Configuration.GetConnectionString("HangfireDb"))));

builder.Services.AddHangfireServer();

builder.Services.AddScoped<IAttendanceJobScheduler, HangfireAttendanceJobScheduler>();
builder.Services.AddScoped<AttendanceJobs>();

var app = builder.Build();

// ===== VERIFICAR Y APLICAR MIGRACIONES PENDIENTES =====
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("DatabaseMigration");
    
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        
        // Verificar si hay migraciones pendientes
        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        
        if (pendingMigrations.Any())
        {
            logger.LogWarning("Se encontraron {Count} migraciones pendientes. Aplicando migraciones...", pendingMigrations.Count());
            foreach (var migration in pendingMigrations)
            {
                logger.LogInformation("Migración pendiente: {Migration}", migration);
            }
            
            // Aplicar migraciones
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Todas las migraciones se aplicaron correctamente");
        }
        else
        {
            logger.LogInformation("La base de datos está actualizada. No hay migraciones pendientes");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al verificar o aplicar migraciones de base de datos");
        throw; // Detener la aplicación si hay un error crítico con las migraciones
    }
}

// ===== INICIALIZAR DATOS DE IDENTITY =====
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("IdentityDataSeeder");
    await AttendanceSystem.Blazor.Server.Data.IdentityDataSeeder.SeedAsync(app.Services, logger);
}


// ===== LOGGING MIDDLEWARE =====
app.UseSerilogRequestLogging();
app.UseMiddleware<RequestLoggingMiddleware>();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// ===== AUTHENTICATION & AUTHORIZATION =====
app.UseAuthentication();
app.UseAuthorization();


// Initial System Configuration for Hangfire
// (Normally we would query DB here to restore schedules, but Hangfire persists them in DB, so no need to re-schedule on startup if using SQL storage)

// Hangfire
app.UseHangfireDashboard("/hangfire");

app.UseAntiforgery();

app.MapRazorComponents<AttendanceSystem.Blazor.Server.Components.App>()
    .AddInteractiveServerRenderMode();


app.MapControllers(); // Map Controllers

// ===== MOSTRAR INFORMACIÓN DE PUERTOS Y CONFIGURACIÓN =====
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("ServerStartup");
    var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigurationRepository>();
    
    try
    {
        // Obtener configuración del sistema
        var systemConfig = await configRepo.GetConfigurationAsync();
        var admsPort = systemConfig?.AdmsPort ?? 16373;
        
        // Obtener las URLs en las que está escuchando el servidor
        var addresses = app.Urls;
        
        logger.LogInformation("========================================");
        logger.LogInformation("🚀 SERVIDOR INICIADO CORRECTAMENTE");
        logger.LogInformation("========================================");
        logger.LogInformation("");
        
        // Mostrar URLs de escucha
        logger.LogInformation("📡 Servidor escuchando en:");
        foreach (var address in addresses)
        {
            var uri = new Uri(address);
            var protocol = uri.Scheme.ToUpper();
            var port = uri.Port;
            
            if (protocol == "HTTPS")
            {
                logger.LogInformation("   🔒 {Protocol}: {Address} (UI - Seguro)", protocol, address);
            }
            else if (port == admsPort)
            {
                logger.LogInformation("   📱 {Protocol}: {Address} (ADMS - Dispositivos)", protocol, address);
            }
            else
            {
                logger.LogInformation("   🌐 {Protocol}: {Address} (UI - Web)", protocol, address);
            }
        }
        
        logger.LogInformation("");
        logger.LogInformation("⚙️  Configuración ADMS:");
        logger.LogInformation("   Puerto configurado: {AdmsPort}", admsPort);
        logger.LogInformation("   URL para dispositivos: http://[IP_SERVIDOR]:{AdmsPort}", admsPort);
        logger.LogInformation("");
        
        // Obtener IP local
        var hostName = System.Net.Dns.GetHostName();
        var localIPs = System.Net.Dns.GetHostEntry(hostName).AddressList
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(ip => ip.ToString())
            .ToList();
        
        if (localIPs.Any())
        {
            logger.LogInformation("🌍 IPs locales detectadas:");
            foreach (var ip in localIPs)
            {
                logger.LogInformation("   • {IP}", ip);
                logger.LogInformation("     - UI Web: http://{IP}:16372", ip);
                logger.LogInformation("     - ADMS: http://{IP}:{AdmsPort}", ip, admsPort);
            }
            logger.LogInformation("");
        }
        
        logger.LogInformation("📊 Servicios disponibles:");
        logger.LogInformation("   • Interfaz Web: {Url}", addresses.FirstOrDefault(a => a.Contains("16372")) ?? "http://localhost:16372");
        logger.LogInformation("   • Hangfire Dashboard: {Url}/hangfire", addresses.FirstOrDefault(a => a.Contains("16372")) ?? "http://localhost:16372");
        logger.LogInformation("   • API ADMS: {Url}", addresses.FirstOrDefault(a => a.Contains(admsPort.ToString())) ?? $"http://localhost:{admsPort}");
        logger.LogInformation("");
        
        logger.LogInformation("========================================");
        logger.LogInformation("✅ Sistema listo para recibir conexiones");
        logger.LogInformation("========================================");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al mostrar información de puertos");
    }
}

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente durante el inicio");
    throw;
}
finally
{
    Log.CloseAndFlush();
}


