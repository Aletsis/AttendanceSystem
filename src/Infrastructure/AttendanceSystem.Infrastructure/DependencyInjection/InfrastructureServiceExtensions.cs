using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Application.Features.Shifts.Queries;
using AttendanceSystem.Application.Features.Branches.Queries;
using AttendanceSystem.Application.Features.Departments.Queries;
using AttendanceSystem.Application.Features.Positions.Queries;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Infrastructure.Adapters;
using AttendanceSystem.Infrastructure.Common;
using AttendanceSystem.Infrastructure.Persistence.Queries;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using AttendanceSystem.ZKTeco.Grpc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registra los servicios de infraestructura, persistencia, adaptadores y clientes de dispositivos
    /// compartidos entre todos los hosts (Blazor Server, WPF, Background Workers).
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(configuration);

        var connectionString = configuration.GetConnectionString("AttendanceDb");

        // 1. Contexto de Base de Datos y Factoría
        services.AddDbContext<AttendanceDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(
                    connectionString,
                    b => b.MigrationsAssembly("AttendanceSystem.Infrastructure"));
            }
        });

        services.AddDbContextFactory<AttendanceDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(
                    connectionString,
                    b => b.MigrationsAssembly("AttendanceSystem.Infrastructure"));
            }
        }, ServiceLifetime.Scoped);

        // Unidad de Trabajo (IUnitOfWork)
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AttendanceDbContext>());

        // 2. Repositorios de Dominio y Datos
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IShiftRosterRepository, ShiftRosterRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IDailyAttendanceRepository, DailyAttendanceRepository>();
        services.AddScoped<ISystemConfigurationRepository, SystemConfigurationRepository>();
        services.AddScoped<IDownloadLogRepository, DownloadLogRepository>();
        services.AddScoped<ISystemAlertRepository, SystemAlertRepository>();
        services.AddScoped<IExternalAttendanceLogRepository, ExternalAttendanceLogRepository>();
        services.AddScoped<IExternalEmployeeRepository, ExternalEmployeeRepository>();
        services.AddScoped<IEmployeeAuditLogRepository, EmployeeAuditLogRepository>();
        services.AddScoped<IAttendanceAdjustmentRepository, AttendanceAdjustmentRepository>();
        services.AddScoped<IAttendanceConsolidationRepository, AttendanceConsolidationRepository>();
        services.AddScoped<IPunchAssignmentRepository, PunchAssignmentRepository>();

        // 3. Consultas CQRS (Dapper / SQL optimizado)
        services.AddScoped<IDeviceQueries, DeviceQueries>();
        services.AddScoped<IShiftQueries, ShiftQueries>();
        services.AddScoped<IBranchQueries, BranchQueries>();
        services.AddScoped<IDepartmentQueries, DepartmentQueries>();
        services.AddScoped<IPositionQueries, PositionQueries>();

        // 4. Clientes de Dispositivos (Transient para garantizar sesiones gRPC y estado mutable independientes - H15)
        services.AddTransient<GrpcZKTecoDeviceClient>();
        services.AddHttpClient<HikvisionDeviceClient>();
        services.AddTransient<AdmsDeviceClient>();

        // Fábrica y Descubrimiento
        services.AddScoped<IDeviceClientFactory, DeviceClientFactory>();
        services.AddScoped<IDeviceDiscoveryService, GrpcZKTecoDiscoveryService>();

        // 5. Servicios del Sistema y Coordinación de Mantenimiento (H05)
        services.AddSingleton<IDeviceLockService, DeviceLockService>();
        // F03-02: el bloqueo vive en la transacción del DbContext del ámbito (pg_advisory_xact_lock).
        services.AddScoped<IAttendanceCalculationLockService>(sp => new PostgresAttendanceCalculationLockService(
            sp.GetRequiredService<AttendanceDbContext>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgresAttendanceCalculationLockService>>(),
            TimeSpan.FromSeconds(configuration.GetValue("AttendanceLocks:TimeoutSeconds", 30))));
        services.AddSingleton<IPersistenceFailureClassifier, NpgsqlPersistenceFailureClassifier>();
        services.AddSingleton<IRestoreStateService, RestoreStateService>();
        services.AddSingleton<ICloudDataSourceProvider, CloudDataSourceProvider>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<ILogTransferService, LogTransferService>();
        services.AddScoped<ICloudLogSyncService, CloudLogSyncService>();
        services.AddSingleton<IAdmsCommandService, AdmsCommandService>();
        services.AddScoped<IReportExportService, ReportExportService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<ITimeNormalizer, TimeNormalizer>();
        services.AddScoped<IAuthoritativeTimeZoneResolver, AuthoritativeTimeZoneResolver>();
        services.AddSingleton<OutboxBackgroundProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxBackgroundProcessor>());

        // F04-05: consumidor durable de consolidación. En hosts sin IHost (WPF) no se ejecuta: las solicitudes
        // quedan en la base de datos y las procesa el servidor.
        var consolidationOptions = configuration.GetSection(AttendanceSystem.Application.Features.Attendance.Consolidation.ConsolidationOptions.SectionName)
            .Get<AttendanceSystem.Application.Features.Attendance.Consolidation.ConsolidationOptions>()
            ?? new AttendanceSystem.Application.Features.Attendance.Consolidation.ConsolidationOptions();
        services.AddSingleton(consolidationOptions);
        services.AddSingleton<ConsolidationWakeupSignal>();
        services.AddSingleton<IConsolidationWakeup>(sp => sp.GetRequiredService<ConsolidationWakeupSignal>());
        services.AddScoped<IOutboxMonitor>(sp => new AttendanceSystem.Infrastructure.Persistence.Outbox.OutboxStore(sp.GetRequiredService<AttendanceDbContext>()));
        services.AddSingleton<AttendanceConsolidationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<AttendanceConsolidationWorker>());

        // 6. Cliente gRPC para Windows Service ZKTeco
        services.AddGrpcClient<ZKTecoService.ZKTecoServiceClient>(options =>
        {
            var url = configuration["ZKTecoService:Url"] ?? "http://localhost:5001";
            options.Address = new Uri(url);
        })
        .ConfigureHttpClient((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var apiKey = config["ZKTecoService:ApiKey"] ?? config["ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            }
        })
        .ConfigureChannel(options =>
        {
            options.MaxReceiveMessageSize = 10 * 1024 * 1024;
            options.MaxSendMessageSize = 10 * 1024 * 1024;
        });

        return services;
    }
}
