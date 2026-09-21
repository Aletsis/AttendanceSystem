using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Logging;

/// <summary>
/// Middleware para registrar detalladamente las solicitudes ADMS (cabeceras, parámetros y payload completo).
/// </summary>
public class AdmsRequestLoggingMiddleware
{
    private static readonly HashSet<string> AdmsRootEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "/cdata",
        "/registry",
        "/push",
        "/querydata",
        "/getrequest",
        "/devicecmd",
        "/ping"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<AdmsRequestLoggingMiddleware> _logger;

    public AdmsRequestLoggingMiddleware(RequestDelegate next, ILogger<AdmsRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Determina si la ruta solicitada corresponde a un endpoint ADMS.
    /// </summary>
    public static bool IsAdmsRequest(PathString path)
    {
        if (path.StartsWithSegments("/iclock", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pathValue = path.Value ?? string.Empty;
        var trimmedPath = pathValue.TrimEnd('/');
        return AdmsRootEndpoints.Contains(trimmedPath);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsAdmsRequest(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var request = context.Request;
        var method = request.Method;
        var pathAndQuery = $"{request.Path}{request.QueryString}";
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        // Formatear cabeceras
        var headersSb = new StringBuilder();
        foreach (var header in request.Headers)
        {
            headersSb.AppendLine($"    {header.Key}: {header.Value}");
        }

        // Leer payload
        string bodyContent = string.Empty;
        request.EnableBuffering();
        if (request.ContentLength is > 0 || request.Body.CanSeek)
        {
            using (var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true))
            {
                bodyContent = await reader.ReadToEndAsync();
                request.Body.Position = 0;
            }
        }

        var bodyDisplay = string.IsNullOrWhiteSpace(bodyContent) ? "[Vacío / Sin payload]" : bodyContent;

        _logger.LogInformation(
            "📥 [ADMS REQUEST] {Method} {PathAndQuery} | IP: {RemoteIp}\n--- CABECERAS ---\n{Headers}\n--- PAYLOAD ---\n{Payload}",
            method,
            pathAndQuery,
            remoteIp,
            headersSb.ToString().TrimEnd(),
            bodyDisplay);

        try
        {
            await _next(context);

            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;

            _logger.LogInformation(
                "📤 [ADMS RESPONSE] {Method} {PathAndQuery} -> Status: {StatusCode} ({ElapsedMilliseconds}ms)",
                method,
                pathAndQuery,
                statusCode,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "❌ [ADMS ERROR] {Method} {PathAndQuery} tras {ElapsedMilliseconds}ms - Error: {ErrorMessage}",
                method,
                pathAndQuery,
                stopwatch.ElapsedMilliseconds,
                ex.Message);

            throw;
        }
    }
}
