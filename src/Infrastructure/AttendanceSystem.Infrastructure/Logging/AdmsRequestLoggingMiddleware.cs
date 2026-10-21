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

        // Formatear cabeceras sanitizando tokens y cookies
        var headersSb = new StringBuilder();
        var sensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "Cookie", "Set-Cookie", "x-api-key", "Proxy-Authorization"
        };

        foreach (var header in request.Headers)
        {
            var headerValue = sensitiveHeaders.Contains(header.Key)
                ? "[REDACTED]"
                : header.Value.ToString();
            headersSb.AppendLine($"    {header.Key}: {headerValue}");
        }

        // Leer y sanitizar payload
        string bodyDisplay;
        request.EnableBuffering();
        if (request.ContentLength is > 0 || request.Body.CanSeek)
        {
            string bodyContent;
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

            if (string.IsNullOrWhiteSpace(bodyContent))
            {
                bodyDisplay = "[Vacío / Sin payload]";
            }
            else
            {
                // Detectar si el payload contiene biometría o contraseñas
                var isSensitiveTable = pathAndQuery.Contains("table=USER", StringComparison.OrdinalIgnoreCase) ||
                                      pathAndQuery.Contains("table=BIODATA", StringComparison.OrdinalIgnoreCase) ||
                                      pathAndQuery.Contains("table=FINGERTMP", StringComparison.OrdinalIgnoreCase) ||
                                      pathAndQuery.Contains("table=BIOPHOTO", StringComparison.OrdinalIgnoreCase) ||
                                      pathAndQuery.Contains("table=USERPIC", StringComparison.OrdinalIgnoreCase) ||
                                      pathAndQuery.Contains("table=TEMPLATE", StringComparison.OrdinalIgnoreCase) ||
                                      pathAndQuery.Contains("table=FINGERPRINT", StringComparison.OrdinalIgnoreCase);

                var containsSensitiveFields = bodyContent.Contains("Passwd", StringComparison.OrdinalIgnoreCase) ||
                                              bodyContent.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                                              bodyContent.Contains("TMP=", StringComparison.OrdinalIgnoreCase) ||
                                              bodyContent.Contains("BIODATA", StringComparison.OrdinalIgnoreCase);

                var isDeviceInfo = pathAndQuery.Contains("devicecmd", StringComparison.OrdinalIgnoreCase) ||
                                   pathAndQuery.Contains("options", StringComparison.OrdinalIgnoreCase);

                if (isDeviceInfo)
                {
                    var sanitized = System.Text.RegularExpressions.Regex.Replace(bodyContent, @"(Passwd|Password)=[^&\r\n\t]+", "$1=***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    const int maxInfoLength = 2048;
                    bodyDisplay = sanitized.Length > maxInfoLength
                        ? $"{sanitized.Substring(0, maxInfoLength)}... [Truncado, total {sanitized.Length} bytes]"
                        : sanitized;
                }
                else if (isSensitiveTable || containsSensitiveFields)
                {
                    bodyDisplay = $"[REDACTED: Contenido sensible protegido ({bodyContent.Length} bytes)]";
                }
                else
                {
                    // Limitar tamaño de logging de payloads a un máximo seguro
                    const int maxLogLength = 512;
                    bodyDisplay = bodyContent.Length > maxLogLength
                        ? $"{bodyContent.Substring(0, maxLogLength)}... [Truncado, total {bodyContent.Length} bytes]"
                        : bodyContent;
                }
            }
        }
        else
        {
            bodyDisplay = "[Vacío / Sin payload]";
        }

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
