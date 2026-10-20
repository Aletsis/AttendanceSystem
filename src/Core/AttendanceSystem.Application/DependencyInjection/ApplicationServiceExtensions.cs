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
            cfg.AddOpenBehavior(typeof(MaintenanceExclusionBehavior<,>));
        });

        // Servicios de Dominio y Validación
        services.AddScoped<AttendanceDeduplicationService>();
        services.AddScoped<AttendanceValidationService>();
        services.AddScoped<AttendanceSystem.Application.Abstractions.IHistoricalAttendanceDiagnosticService, AttendanceSystem.Application.Features.Attendance.Services.HistoricalAttendanceTimeDiagnosticService>();
        services.AddScoped<AttendanceSystem.Application.Abstractions.IPunchAssignmentDiagnosticService, AttendanceSystem.Application.Features.Attendance.Services.PunchAssignmentDiagnosticService>();

        return services;
    }
}
