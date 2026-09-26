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

            // Interceptar peticiones ADMS de terminales biométricos (/iclock/*)
            if (path.StartsWith("/iclock", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/cdata", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/getrequest", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/devicecmd", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rechazando petición ADMS {Path} con HTTP 503: Base de datos en proceso de restauración.", path);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Service Unavailable: Database restore in progress");
                return;
            }
        }

        await _next(context);
    }
}
