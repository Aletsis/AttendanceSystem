using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public class AdmsCommandService : IAdmsCommandService
{
    private record QueuedCommand(Guid CommandGuid, string CommandText, Guid? DownloadLogId);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdmsCommandService> _logger;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueuedCommand>> _commandQueues = new(StringComparer.OrdinalIgnoreCase);

    // Map (SerialNumber, CommandID) -> DownloadLogId
    private readonly ConcurrentDictionary<string, Guid> _pendingExecutions = new(StringComparer.OrdinalIgnoreCase);

    private class AdmsDownloadSession
    {
        public Guid DownloadLogId { get; init; }
        public string SerialNumber { get; init; } = "";
        public string CommandId { get; init; } = "";
        public int TotalRecordsReceived;
        public int NewRecordsInserted;
        public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, AdmsDownloadSession> _activeDownloadSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _latestActiveSessionBySn = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, Guid, bool, int, int, string?>? OnDownloadStatusChanged;

    // SerialNumber -> TaskCompletionSource<DeviceInfoDto?>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DeviceInfoDto?>> _deviceInfoWaiters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string CommandId, TaskCompletionSource<DeviceInfoDto?> Waiter)> _deviceInfoExecutions = new(StringComparer.OrdinalIgnoreCase);

    private static int _commandCounter = 10000;

    public AdmsCommandService(IServiceScopeFactory scopeFactory, ILogger<AdmsCommandService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Cargar comandos pendientes de base de datos en caso de reinicio del servicio
        _ = Task.Run(LoadPendingCommandsFromDatabaseAsync);
    }

    private async Task LoadPendingCommandsFromDatabaseAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            // Recuperar comandos pendientes y comandos enviados que expiraron sin acuse (> 10 minutos)
            var staleThreshold = DateTime.UtcNow.AddMinutes(-10);
            var commands = await dbContext.AdmsCommands
                .Where(c => c.Status == AdmsCommandStatus.Pending ||
                           (c.Status == AdmsCommandStatus.Sent && c.SentAtUtc < staleThreshold))
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync();

            foreach (var cmd in commands)
            {
                if (cmd.Status == AdmsCommandStatus.Sent)
                {
                    if (cmd.CanRetryAutomatically())
                    {
                        cmd.ResetForRetry();
                        _logger.LogInformation("Comando ADMS pendiente recuperado tras reinicio/expiración: {SN} {Text}", cmd.SerialNumber, cmd.CommandText);
                    }
                    else
                    {
                        cmd.MarkAsExpired();
                        continue;
                    }
                }

                var queue = _commandQueues.GetOrAdd(cmd.SerialNumber, _ => new ConcurrentQueue<QueuedCommand>());
                queue.Enqueue(new QueuedCommand(cmd.Id, cmd.CommandText, cmd.DownloadLogId));
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Advertencia cargando comandos ADMS pendientes de BD en arranque");
        }
    }

    public void EnqueueCommand(string serialNumber, string command, Guid? downloadLogId = null)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return;

        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        var tempCmdId = "0"; // Se asigna número correlativo único al momento de enviar

        var admsCommand = AdmsCommand.Create(cleanSn, tempCmdId, command, downloadLogId);

        // Guardar de forma duradera en PostgreSQL
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
                await dbContext.AdmsCommands.AddAsync(admsCommand);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al persistir comando ADMS para SN {SN} en base de datos", cleanSn);
            }
        });

        var queue = _commandQueues.GetOrAdd(cleanSn, _ => new ConcurrentQueue<QueuedCommand>());
        queue.Enqueue(new QueuedCommand(admsCommand.Id, command, downloadLogId));
    }

    public (string? CommandId, string? Command, Guid? DownloadLogId) GetNextCommand(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return (null, null, null);

        var cleanSn = serialNumber.Trim().ToUpperInvariant();

        if (_commandQueues.TryGetValue(cleanSn, out var queue))
        {
            if (queue.TryDequeue(out var cmd))
            {
                // Generar ID no colisionable entre 10000 y 99999
                var rawId = (Interlocked.Increment(ref _commandCounter) % 90000) + 10000;
                var commandId = rawId.ToString();

                if (cmd.CommandText.Equals("INFO", StringComparison.OrdinalIgnoreCase) &&
                    _deviceInfoWaiters.TryGetValue(cleanSn, out var infoWaiter))
                {
                    _deviceInfoExecutions[cleanSn] = (commandId, infoWaiter);
                }

                if (cmd.DownloadLogId.HasValue)
                {
                    RegisterPendingExecution(cleanSn, commandId, cmd.DownloadLogId.Value);
                }

                // Actualizar comando a estado Sent en base de datos
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
                        var dbCmd = await dbContext.AdmsCommands.FindAsync(cmd.CommandGuid);
                        if (dbCmd != null)
                        {
                            typeof(AdmsCommand).GetProperty(nameof(AdmsCommand.CommandId))?
                                .SetValue(dbCmd, commandId);
                            dbCmd.MarkAsSent();
                            await dbContext.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error actualizando estado Sent de comando ADMS {Guid}", cmd.CommandGuid);
                    }
                });

                return (commandId, cmd.CommandText, cmd.DownloadLogId);
            }
        }

        return (null, null, null);
    }

    public bool HasPendingCommands(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return false;

        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        return _commandQueues.TryGetValue(cleanSn, out var queue) && !queue.IsEmpty;
    }

    public void RegisterPendingExecution(string serialNumber, string commandId, Guid downloadLogId)
    {
        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        var key = $"{cleanSn}:{commandId}";
        _pendingExecutions[key] = downloadLogId;
        // Compatibilidad para búsqueda solo por ID
        _pendingExecutions[commandId] = downloadLogId;

        var session = new AdmsDownloadSession
        {
            DownloadLogId = downloadLogId,
            SerialNumber = cleanSn,
            CommandId = commandId
        };
        _activeDownloadSessions[key] = session;
        _activeDownloadSessions[commandId] = session;
        _latestActiveSessionBySn[cleanSn] = commandId;
    }

    public Guid? GetActiveDownloadLogId(string serialNumber, string? commandId = null)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;
        var cleanSn = serialNumber.Trim().ToUpperInvariant();

        if (!string.IsNullOrEmpty(commandId))
        {
            var key = $"{cleanSn}:{commandId}";
            if (_activeDownloadSessions.TryGetValue(key, out var session))
                return session.DownloadLogId;
            if (_activeDownloadSessions.TryGetValue(commandId, out var fallback))
                return fallback.DownloadLogId;
        }

        if (_latestActiveSessionBySn.TryGetValue(cleanSn, out var activeCmdId))
        {
            var key = $"{cleanSn}:{activeCmdId}";
            if (_activeDownloadSessions.TryGetValue(key, out var session))
                return session.DownloadLogId;
        }

        return null;
    }

    public void RecordDownloadProgress(string serialNumber, string? commandId, int totalReceived, int newInserted)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return;
        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        AdmsDownloadSession? session = null;

        if (!string.IsNullOrEmpty(commandId))
        {
            var key = $"{cleanSn}:{commandId}";
            _activeDownloadSessions.TryGetValue(key, out session);
            if (session == null) _activeDownloadSessions.TryGetValue(commandId, out session);
        }

        if (session == null && _latestActiveSessionBySn.TryGetValue(cleanSn, out var activeCmdId))
        {
            var key = $"{cleanSn}:{activeCmdId}";
            _activeDownloadSessions.TryGetValue(key, out session);
        }

        if (session != null)
        {
            Interlocked.Add(ref session.TotalRecordsReceived, totalReceived);
            Interlocked.Add(ref session.NewRecordsInserted, newInserted);
            _logger.LogInformation("📊 ADMS Progreso SN:{SN} CMD:{CmdId} - Recibidos: +{Rec} (Total: {Tot}), Nuevos: +{New} (Total: {TotNew})",
                cleanSn, session.CommandId, totalReceived, session.TotalRecordsReceived, newInserted, session.NewRecordsInserted);
        }
    }

    public (int TotalReceived, int NewInserted) GetAndCompleteDownloadSession(string serialNumber, string commandId)
    {
        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        var key = $"{cleanSn}:{commandId}";
        AdmsDownloadSession? session = null;

        if (_activeDownloadSessions.TryRemove(key, out session))
        {
            _activeDownloadSessions.TryRemove(commandId, out _);
        }
        else if (_activeDownloadSessions.TryRemove(commandId, out session))
        {
            // fallback
        }

        if (_latestActiveSessionBySn.TryGetValue(cleanSn, out var lastCmd) && lastCmd == commandId)
        {
            _latestActiveSessionBySn.TryRemove(cleanSn, out _);
        }

        if (session != null)
        {
            return (session.TotalRecordsReceived, session.NewRecordsInserted);
        }

        return (0, 0);
    }

    public void NotifyDownloadCompleted(string serialNumber, Guid downloadLogId, bool isSuccess, int totalDownloaded, int newRecords, string? errorMessage = null)
    {
        try
        {
            OnDownloadStatusChanged?.Invoke(serialNumber, downloadLogId, isSuccess, totalDownloaded, newRecords, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al invocar OnDownloadStatusChanged para SN:{SN} Log:{LogId}", serialNumber, downloadLogId);
        }
    }

    public Guid? GetAndRemovePendingExecution(string serialNumber, string commandId)
    {
        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        var key = $"{cleanSn}:{commandId}";

        if (_pendingExecutions.TryRemove(key, out var logId))
        {
            _pendingExecutions.TryRemove(commandId, out _);
            return logId;
        }

        if (_pendingExecutions.TryRemove(commandId, out var fallbackLogId))
        {
            return fallbackLogId;
        }

        return null;
    }

    public Guid? GetAndRemovePendingExecution(string commandId)
    {
        if (_pendingExecutions.TryRemove(commandId, out var logId))
        {
            return logId;
        }
        return null;
    }

    public async Task<Guid?> AcknowledgeCommandExecutionAsync(
        string serialNumber,
        string commandId,
        int returnCode,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        var logId = GetAndRemovePendingExecution(cleanSn, commandId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            var dbCmd = await dbContext.AdmsCommands
                .Where(c => c.SerialNumber == cleanSn && c.CommandId == commandId && c.Status == AdmsCommandStatus.Sent)
                .OrderByDescending(c => c.SentAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (dbCmd != null)
            {
                if (returnCode >= 0 || returnCode == -2)
                {
                    dbCmd.MarkAsConfirmed(returnCode);
                }
                else
                {
                    dbCmd.MarkAsFailed(returnCode, errorMessage);
                    if (dbCmd.CanRetryAutomatically())
                    {
                        dbCmd.ResetForRetry();
                        var queue = _commandQueues.GetOrAdd(cleanSn, _ => new ConcurrentQueue<QueuedCommand>());
                        queue.Enqueue(new QueuedCommand(dbCmd.Id, dbCmd.CommandText, dbCmd.DownloadLogId));
                        _logger.LogInformation("Comando ADMS fallido reencolado automáticamente para SN {SN}: {Cmd}", cleanSn, dbCmd.CommandText);
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return logId ?? dbCmd.DownloadLogId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al confirmar comando ADMS SN:{SN} ID:{ID} en BD", cleanSn, commandId);
        }

        return logId;
    }

    public async Task<DeviceInfoDto?> WaitForDeviceInfoAsync(string serialNumber, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;

        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        var tcs = new TaskCompletionSource<DeviceInfoDto?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _deviceInfoWaiters.AddOrUpdate(cleanSn, tcs, (_, oldTcs) =>
        {
            oldTcs.TrySetResult(null);
            return tcs;
        });

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            using (cts.Token.Register(() => tcs.TrySetResult(null)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _deviceInfoWaiters.TryRemove(new KeyValuePair<string, TaskCompletionSource<DeviceInfoDto?>>(cleanSn, tcs));
            if (_deviceInfoExecutions.TryGetValue(cleanSn, out var execution) && ReferenceEquals(execution.Waiter, tcs))
            {
                _deviceInfoExecutions.TryRemove(new KeyValuePair<string, (string, TaskCompletionSource<DeviceInfoDto?>)>(cleanSn, execution));
            }
        }
    }

    public void NotifyDeviceInfoReceived(string serialNumber, DeviceInfoDto deviceInfo, string? commandId = null)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return;

        var cleanSn = serialNumber.Trim().ToUpperInvariant();
        if (_deviceInfoWaiters.TryGetValue(cleanSn, out var tcs) &&
            _deviceInfoExecutions.TryGetValue(cleanSn, out var execution) &&
            execution.CommandId == commandId && ReferenceEquals(execution.Waiter, tcs))
        {
            tcs.TrySetResult(deviceInfo);
        }
    }
}
