using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Behaviors;
using AttendanceSystem.Domain.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.Application.DependencyInjection;

public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registra los servicios de la capa de aplicación y dominio comunes para todos los hosts (Blazor, WPF, Tests).
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Registrar MediatR con handlers del ensamblado de Application
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceExtensions).Assembly);
            // Caso 13: sin el permiso que pide la petición no se valida, no se registra escritura ni se ejecuta.
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            // F07-02: un rango inválido se rechaza antes de cualquier otra etapa (no registra escrituras ni lee datos).
            cfg.AddOpenBehavior(typeof(DateRangeValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(MaintenanceExclusionBehavior<,>));
        });

        services.AddScoped<AttendanceSystem.Application.Authorization.IPermissionService>(sp =>
            new AttendanceSystem.Application.Authorization.PermissionService(sp.GetService<ICurrentUserService>()));

        // Servicios de Dominio y Validación
        services.AddScoped<AttendanceDeduplicationService>();
        services.AddScoped<AttendanceSystem.Application.Features.Devices.Clock.DeviceClockService>();
        services.AddScoped<AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService>();
        services.AddScoped<AttendanceValidationService>();
        services.AddScoped<AttendanceSystem.Application.Abstractions.IHistoricalAttendanceDiagnosticService, AttendanceSystem.Application.Features.Attendance.Services.HistoricalAttendanceTimeDiagnosticService>();
        services.AddScoped<AttendanceSystem.Application.Abstractions.IPunchAssignmentDiagnosticService, AttendanceSystem.Application.Features.Attendance.Services.PunchAssignmentDiagnosticService>();
        services.AddScoped<AttendanceSystem.Application.Features.Attendance.Consolidation.AttendanceConsolidationProcessor>();
        services.AddScoped<AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionService>();
        services.AddScoped<AttendanceSystem.Application.Features.Attendance.Consolidation.RetroactiveRecalculation>();
        services.AddScoped<AttendanceSystem.Application.Features.Attendance.PeriodClosure.PeriodClosureService>();
        services.AddScoped<AttendanceSystem.Application.Abstractions.IConsolidationRequestTracker, AttendanceSystem.Application.Abstractions.ConsolidationRequestTracker>();
        services.AddScoped<AttendanceSystem.Application.Abstractions.IAdjustmentDiagnosticService, AttendanceSystem.Application.Features.Attendance.Services.AdjustmentDiagnosticService>();

        return services;
    }
}
