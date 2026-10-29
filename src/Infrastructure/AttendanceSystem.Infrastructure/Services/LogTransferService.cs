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

    private readonly ITimeNormalizer _timeNormalizer;
    private readonly ICloudDataSourceProvider _dataSourceProvider;

    public LogTransferService(
        ISystemConfigurationRepository configurationRepository,
        IExternalAttendanceLogRepository externalLogRepository,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        ICloudDataSourceProvider dataSourceProvider,
        ILogger<LogTransferService> logger)
    {
        _configurationRepository = configurationRepository;
        _externalLogRepository = externalLogRepository;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _dataSourceProvider = dataSourceProvider;
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
        Guid? logId = null,
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

            // F06-P03 / H22: Reutilizar NpgsqlDataSource desde el provider singleton para no agotar ni recrear pools
            var dataSource = _dataSourceProvider.GetDataSource(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            // Solo aplicar DDL si aún no ha sido asegurado en esta sesión para esta base de datos
            if (!_dataSourceProvider.IsSchemaEnsured(connStr))
            {
                await EnsureSchemaAsync(conn, cancellationToken);
                _dataSourceProvider.MarkSchemaEnsured(connStr);
            }

            var targetId = logId ?? Guid.NewGuid();
            await InsertAsync(conn, targetId, branchCode, employeeId, checkTime, verifyMethod, checkType, sourceDevice, cancellationToken);

            _logger.LogInformation("Log para empleado externo {EmployeeId} (Sucursal: {BranchCode}) transferido a la BD en la nube. ID: {LogId}",
                employeeId, branchCode, targetId);

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

            // Sin nube activa o configurada no se toca ningún log: no es culpa del registro y no debe contar como intento.
            var config = await _configurationRepository.GetConfigurationAsync(cancellationToken);
            if (config != null && !config.IsCloudSyncEnabled)
            {
                _logger.LogWarning("Hay {Count} logs externos pendientes, pero la sincronización en la nube está desactivada.", pendingLogs.Count);
                return Result<int>.Success(0);
            }

            var connStr = await GetConnectionStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogWarning("Hay {Count} logs externos pendientes, pero la cadena de conexión a la nube no está configurada.", pendingLogs.Count);
                return Result<int>.Success(0);
            }

            _logger.LogInformation("Reintentando transferencia de {Count} logs externos pendientes a la nube...", pendingLogs.Count);

            // Una sola conexión para el lote: si la nube no responde se falla una vez, sin marcar los logs.
            var dataSource = _dataSourceProvider.GetDataSource(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
            if (!_dataSourceProvider.IsSchemaEnsured(connStr))
            {
                await EnsureSchemaAsync(conn, cancellationToken);
                _dataSourceProvider.MarkSchemaEnsured(connStr);
            }

            int successCount = 0;
            string? connectionError = null;
            foreach (var log in pendingLogs)
            {
                try
                {
                    await InsertAsync(conn, log.Id, log.BranchCode, log.EmployeeId, log.CheckTime,
                        log.VerifyMethod, log.CheckType, log.SourceDevice, cancellationToken);
                    log.MarkAsTransferred();
                    successCount++;
                }
                catch (PostgresException ex)
                {
                    // El servidor rechazó este registro en particular; los demás siguen.
                    _logger.LogWarning(ex, "La nube rechazó el log externo {LogId} del empleado {EmployeeId}", log.Id, log.EmployeeId);
                    log.MarkAsFailed($"Error al escribir en la BD en la nube: {ex.Message}");
                }
                catch (NpgsqlException ex)
                {
                    // Se perdió la conexión: se detiene el lote y lo restante queda para el siguiente ciclo.
                    connectionError = ex.Message;
                    break;
                }

                await _externalLogRepository.UpdateAsync(log, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reintento finalizado: {SuccessCount} de {Total} logs transferidos exitosamente",
                successCount, pendingLogs.Count);

            if (connectionError != null)
                return Result<int>.Failure($"Se perdió la conexión con la BD en la nube: {connectionError}");

            return Result<int>.Success(successCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el reintento de logs externos pendientes");
            return Result<int>.Failure(ex.Message);
        }
    }

    private async Task InsertAsync(
        NpgsqlConnection conn,
        Guid id,
        string branchCode,
        string employeeId,
        DateTime checkTime,
        int verifyMethod,
        int checkType,
        string? sourceDevice,
        CancellationToken cancellationToken)
    {
        // Sin destino en ON CONFLICT: un reenvío del mismo id o la misma checada (sucursal, empleado, hora)
        // con otro id se ignora en lugar de fallar para siempre.
        const string insertSql = @"
            INSERT INTO external_attendance_logs 
            (id, branch_code, employee_id, check_time, verify_method, check_type, source_device, created_at, is_downloaded)
            VALUES (@id, @branchCode, @employeeId, @checkTime, @verifyMethod, @checkType, @sourceDevice, NOW(), FALSE)
            ON CONFLICT DO NOTHING;";

        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("branchCode", branchCode.ToUpperInvariant());
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("checkTime", _timeNormalizer.ToUtc(checkTime));
        cmd.Parameters.AddWithValue("verifyMethod", verifyMethod);
        cmd.Parameters.AddWithValue("checkType", checkType);
        cmd.Parameters.AddWithValue("sourceDevice", (object?)sourceDevice ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
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

            // Reutilizar NpgsqlDataSource desde el provider singleton
            var dataSource = _dataSourceProvider.GetDataSource(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);

            await EnsureSchemaAsync(conn, cancellationToken);
            _dataSourceProvider.MarkSchemaEnsured(connStr);

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
                downloaded_by_branch VARCHAR(10),
                claim_token VARCHAR(50),
                claim_expires_at TIMESTAMP WITH TIME ZONE,
                download_attempts INT NOT NULL DEFAULT 0
            );

            ALTER TABLE external_attendance_logs ADD COLUMN IF NOT EXISTS download_attempts INT NOT NULL DEFAULT 0;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_ext_logs_branch_emp_time 
                ON external_attendance_logs(branch_code, employee_id, check_time);

            CREATE INDEX IF NOT EXISTS idx_ext_logs_branch_downloaded 
                ON external_attendance_logs(branch_code, is_downloaded);";

        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
