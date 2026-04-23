using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace AttendanceSystem.Infrastructure.Services;

public class LogTransferService : ILogTransferService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LogTransferService> _logger;

    public LogTransferService(HttpClient httpClient, ILogger<LogTransferService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<bool>> TransferLogAsync(
        string host, 
        string employeeId, 
        DateTime checkTime, 
        int verifyMethod, 
        int checkType, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Asegurar que el host termina sin slash
            var baseUrl = host.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/external-logs/receive";

            var payload = new
            {
                EmployeeId = employeeId,
                CheckTime = checkTime,
                VerifyMethod = verifyMethod,
                CheckType = checkType
            };

            _logger.LogInformation("Enviando log de empleado {Id} a {Endpoint}", employeeId, endpoint);

            var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Log transferido exitosamente a {Host}", host);
                return Result<bool>.Success(true);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Fallo al transferir log a {Host}. Status: {Status}. Error: {Error}", 
                host, response.StatusCode, errorContent);

            return Result<bool>.Failure($"Error {response.StatusCode}: {errorContent}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al transferir log a {Host}", host);
            return Result<bool>.Failure(ex.Message);
        }
    }
}
