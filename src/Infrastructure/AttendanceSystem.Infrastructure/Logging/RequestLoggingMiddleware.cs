using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AttendanceSystem.Infrastructure.Logging;

/// <summary>
/// Middleware para registrar todas las solicitudes HTTP
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var requestMethod = context.Request.Method;

        try
        {
            _logger.LogInformation(
                "Iniciando solicitud HTTP: {Method} {Path}",
                requestMethod,
                requestPath);

            await _next(context);

            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var logLevel = statusCode >= 500 ? LogLevel.Error :
                          statusCode >= 400 ? LogLevel.Warning :
                          LogLevel.Information;

            _logger.Log(
                logLevel,
                "Solicitud HTTP completada: {Method} {Path} - Status: {StatusCode} - Tiempo: {ElapsedMilliseconds}ms",
                requestMethod,
                requestPath,
                statusCode,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Error en solicitud HTTP: {Method} {Path} - Tiempo: {ElapsedMilliseconds}ms - Error: {ErrorMessage}",
                requestMethod,
                requestPath,
                stopwatch.ElapsedMilliseconds,
                ex.Message);

            throw;
        }
    }
}
