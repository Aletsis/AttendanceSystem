using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

public class LogTransferService : ILogTransferService
{
    private readonly ISystemConfigurationRepository _configurationRepository;
    private readonly IExternalAttendanceLogRepository _externalLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LogTransferService> _logger;

    public LogTransferService(
        ISystemConfigurationRepository configurationRepository,
        IExternalAttendanceLogRepository externalLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<LogTransferService> logger)
    {
        _configurationRepository = configurationRepository;
        _externalLogRepository = externalLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    private async Task<string?> GetConnectionStringAsync(CancellationToken cancellationToken)
    {
        var config = await _configurationRepository.GetConfigurationAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(config?.CloudDbConnectionString))
        {
            return config.CloudDbConnectionString;
        }

        return null;
    }

    public async Task<Result<bool>> TransferLogAsync(
        string branchCode,
        string employeeId,
        DateTime checkTime,
        int verifyMethod,
        int checkType,
        string? sourceDevice = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configurationRepository.GetConfigurationAsync(cancellationToken);
            if (config != null && !config.IsCloudSyncEnabled)
            {
                _logger.LogWarning("Sincronización en la nube desactivada en la configuración del sistema.");
                return Result<bool>.Failure("La sincronización en la nube se encuentra desactivada.");
            }

            var connStr = await GetConnectionStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogWarning("Cadena de conexión a la nube no configurada.");
                return Result<bool>.Failure("La cadena de conexión a la base de datos en la nube no está configurada.");
            }

            await using var dataSource = NpgsqlDataSource.Create(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            await EnsureSchemaAsync(conn, cancellationToken);

            const string insertSql = @"
                INSERT INTO external_attendance_logs 
                (id, branch_code, employee_id, check_time, verify_method, check_type, source_device, created_at, is_downloaded)
                VALUES (@id, @branchCode, @employeeId, @checkTime, @verifyMethod, @checkType, @sourceDevice, NOW(), FALSE);";

            var logId = Guid.NewGuid();
            var utcCheckTime = checkTime.Kind == DateTimeKind.Utc 
                ? checkTime 
                : DateTime.SpecifyKind(checkTime, DateTimeKind.Utc);

            await using var cmd = new NpgsqlCommand(insertSql, conn);
            cmd.Parameters.AddWithValue("id", logId);
            cmd.Parameters.AddWithValue("branchCode", branchCode.ToUpperInvariant());
            cmd.Parameters.AddWithValue("employeeId", employeeId);
            cmd.Parameters.AddWithValue("checkTime", utcCheckTime);
            cmd.Parameters.AddWithValue("verifyMethod", verifyMethod);
            cmd.Parameters.AddWithValue("checkType", checkType);
            cmd.Parameters.AddWithValue("sourceDevice", (object?)sourceDevice ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Log para empleado externo {EmployeeId} (Sucursal: {BranchCode}) transferido a la BD en la nube. ID: {LogId}",
                employeeId, branchCode, logId);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al transferir log de empleado externo {EmployeeId} a la BD en la nube", employeeId);
            return Result<bool>.Failure($"Error al escribir en la BD en la nube: {ex.Message}");
        }
    }

    public async Task<Result<int>> RetryPendingTransfersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingLogs = await _externalLogRepository.GetPendingOrFailedLogsAsync(100, cancellationToken);
            if (!pendingLogs.Any())
                return Result<int>.Success(0);

            _logger.LogInformation("Reintentando transferencia de {Count} logs externos pendientes a la nube...", pendingLogs.Count);

            int successCount = 0;
            foreach (var log in pendingLogs)
            {
                var result = await TransferLogAsync(
                    log.BranchCode,
                    log.EmployeeId,
                    log.CheckTime,
                    log.VerifyMethod,
                    log.CheckType,
                    log.SourceDevice,
                    cancellationToken);

                if (result.IsSuccess)
                {
                    log.MarkAsTransferred();
                    successCount++;
                }
                else
                {
                    log.MarkAsFailed(result.Error);
                }

                await _externalLogRepository.UpdateAsync(log, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reintento finalizado: {SuccessCount} de {Total} logs transferidos exitosamente",
                successCount, pendingLogs.Count);

            return Result<int>.Success(successCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el reintento de logs externos pendientes");
            return Result<int>.Failure(ex.Message);
        }
    }

    public async Task<Result<bool>> TestConnectionAsync(string? connectionString = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var connStr = !string.IsNullOrWhiteSpace(connectionString)
                ? connectionString
                : await GetConnectionStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(connStr))
            {
                return Result<bool>.Failure("La cadena de conexión a la base de datos en la nube no está configurada.");
            }

            await using var dataSource = NpgsqlDataSource.Create(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);

            await EnsureSchemaAsync(conn, cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prueba de conexión a BD en la nube fallida");
            return Result<bool>.Failure(ex.Message);
        }
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        const string ddl = @"
            CREATE TABLE IF NOT EXISTS external_attendance_logs (
                id UUID PRIMARY KEY,
                branch_code VARCHAR(10) NOT NULL,
                employee_id VARCHAR(50) NOT NULL,
                check_time TIMESTAMP WITH TIME ZONE NOT NULL,
                verify_method INT NOT NULL,
                check_type INT NOT NULL,
                source_device VARCHAR(100),
                origin_branch_code VARCHAR(10),
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                is_downloaded BOOLEAN NOT NULL DEFAULT FALSE,
                downloaded_at TIMESTAMP WITH TIME ZONE,
                downloaded_by_branch VARCHAR(10)
            );

            CREATE INDEX IF NOT EXISTS idx_ext_logs_branch_downloaded 
                ON external_attendance_logs(branch_code, is_downloaded);";

        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
