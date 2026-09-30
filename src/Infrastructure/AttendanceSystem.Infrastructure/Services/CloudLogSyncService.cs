using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

public class CloudLogSyncService : ICloudLogSyncService
{
    private readonly ISystemConfigurationRepository _configurationRepository;
    private readonly ISender _sender;
    private readonly ILogger<CloudLogSyncService> _logger;

    public CloudLogSyncService(
        ISystemConfigurationRepository configurationRepository,
        ISender sender,
        ILogger<CloudLogSyncService> logger)
    {
        _configurationRepository = configurationRepository;
        _sender = sender;
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

    public async Task<Result<IReadOnlyList<CloudLogItemDto>>> FetchPendingLogsAsync(string branchCode, CancellationToken cancellationToken = default)
    {
        try
        {
            var connStr = await GetConnectionStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return Result<IReadOnlyList<CloudLogItemDto>>.Failure("La cadena de conexión a la base de datos en la nube no está configurada.");
            }

            await using var dataSource = NpgsqlDataSource.Create(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT id, branch_code, employee_id, check_time, verify_method, check_type, source_device, created_at
                FROM external_attendance_logs
                WHERE branch_code = @branchCode AND is_downloaded = FALSE
                ORDER BY check_time ASC;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("branchCode", branchCode.ToUpperInvariant());

            var items = new List<CloudLogItemDto>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CloudLogItemDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDateTime(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetDateTime(7)
                ));
            }

            return Result<IReadOnlyList<CloudLogItemDto>>.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar logs pendientes en la nube para la sucursal {BranchCode}", branchCode);
            return Result<IReadOnlyList<CloudLogItemDto>>.Failure(ex.Message);
        }
    }

    public async Task<Result<int>> SyncPendingLogsForBranchesAsync(IEnumerable<string> branchCodes, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await _configurationRepository.GetConfigurationAsync(cancellationToken);
            if (config != null && !config.IsCloudSyncEnabled)
            {
                _logger.LogInformation("Sincronización en la nube desactivada en la configuración.");
                return Result<int>.Success(0);
            }

            int totalSynced = 0;

            foreach (var code in branchCodes)
            {
                var fetchResult = await FetchPendingLogsAsync(code, cancellationToken);
                if (!fetchResult.IsSuccess || !fetchResult.Value.Any())
                    continue;

                var pendingLogs = fetchResult.Value;
                _logger.LogInformation("Descargando {Count} logs de la nube para sucursal local {Code}...", pendingLogs.Count, code);

                var downloadedIds = new List<Guid>();

                foreach (var log in pendingLogs)
                {
                    var sourceDeviceId = string.IsNullOrWhiteSpace(log.SourceDevice)
                        ? "CLOUD_SYNC"
                        : $"CLOUD_{log.SourceDevice}";

                    var recordCommand = new RecordAttendanceCommand(
                        log.EmployeeId,
                        sourceDeviceId,
                        log.CheckTime,
                        log.VerifyMethod,
                        log.CheckType
                    );

                    var recordResult = await _sender.Send(recordCommand, cancellationToken);
                    if (recordResult.IsSuccess)
                    {
                        downloadedIds.Add(log.Id);
                        totalSynced++;
                    }
                    else
                    {
                        // Si falla porque ya existía (duplicado), aún así lo marcamos como descargado para no reintentarlo eternamente
                        if (recordResult.Error.Contains("duplicad", StringComparison.OrdinalIgnoreCase) ||
                            recordResult.Error.Contains("ya existe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadedIds.Add(log.Id);
                        }
                        else
                        {
                            _logger.LogWarning("No se pudo registrar log de empleado {EmpId} de la nube: {Error}",
                                log.EmployeeId, recordResult.Error);
                        }
                    }
                }

                if (downloadedIds.Any())
                {
                    await MarkLogsAsDownloadedAsync(downloadedIds, code, cancellationToken);
                }
            }

            _logger.LogInformation("Sincronización desde la nube completada: {Count} registros importados", totalSynced);
            return Result<int>.Success(totalSynced);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al sincronizar logs desde la nube");
            return Result<int>.Failure(ex.Message);
        }
    }

    public async Task<Result<bool>> MarkLogsAsDownloadedAsync(IEnumerable<Guid> logIds, string downloadedByBranch, CancellationToken cancellationToken = default)
    {
        try
        {
            var idsList = logIds.ToList();
            if (!idsList.Any())
                return Result<bool>.Success(true);

            var connStr = await GetConnectionStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(connStr))
                return Result<bool>.Failure("La cadena de conexión a la base de datos en la nube no está configurada.");

            await using var dataSource = NpgsqlDataSource.Create(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = @"
                UPDATE external_attendance_logs
                SET is_downloaded = TRUE,
                    downloaded_at = NOW(),
                    downloaded_by_branch = @branchCode
                WHERE id = ANY(@ids);";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("branchCode", downloadedByBranch.ToUpperInvariant());
            cmd.Parameters.AddWithValue("ids", idsList.ToArray());

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al marcar logs como descargados en la nube");
            return Result<bool>.Failure(ex.Message);
        }
    }
}
