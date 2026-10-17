using AttendanceSystem.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Blazor.Server.Middleware;

/// <summary>
/// Middleware que intercepta solicitudes entrantes de hardware biométrico (ADMS) y APIs de base de datos
/// cuando una restauración de la base de datos se encuentra en curso, respondiendo con HTTP 503.
/// </summary>
public class RestoreMaintenanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RestoreMaintenanceMiddleware> _logger;

    public RestoreMaintenanceMiddleware(
        RequestDelegate next,
        ILogger<RestoreMaintenanceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRestoreStateService restoreState)
    {
        if (restoreState.IsRestoreInProgress)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Interceptar peticiones ADMS de terminales biométricos y APIs de datos
            if (path.StartsWith("/iclock", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/cdata", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/getrequest", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/devicecmd", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/registry", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/push", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/querydata", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/ping", StringComparison.OrdinalIgnoreCase) ||
                (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/api/backup", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Rechazando petición {Path} con HTTP 503: Base de datos en proceso de restauración ({State}).", path, restoreState.CurrentState);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain";
                context.Response.Headers.RetryAfter = "30";
                await context.Response.WriteAsync($"Service Unavailable: Database restore in progress ({restoreState.CurrentState})");
                return;
            }
        }

        await _next(context);
    }
}
