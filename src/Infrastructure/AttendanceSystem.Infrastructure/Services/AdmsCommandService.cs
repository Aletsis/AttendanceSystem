using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Cola de comandos ADMS sobre la tabla AdmsCommands. La fila es la fuente de verdad: cualquier proceso encola (la web,
/// sus workers o el cliente WPF) y el que atiende al reloj la reclama con <c>SKIP LOCKED</c>, así que un comando se
/// entrega una sola vez aunque haya varios procesos. La respuesta a INFO vuelve en la misma fila (ResultPayload) a quien
/// la espera, en este u otro proceso. Las sesiones de descarga (contadores de lo recibido) quedan en memoria del proceso
/// que atiende al reloj: el reloj entrega datos y acuse al mismo servidor al que pidió el comando.
/// </summary>
public class AdmsCommandService : IAdmsCommandService
{
    /// <summary>Un comando entregado sin acuse en este tiempo se da por perdido (el reloj se reinició o lo descartó).</summary>
    public static readonly TimeSpan SentCommandTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DeviceInfoPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdmsCommandService> _logger;

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

    // Aviso inmediato cuando la respuesta a INFO llega en este mismo proceso; entre procesos se consulta la fila.
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _deviceInfoSignals = new();

    // Punto de partida aleatorio: dos procesos no reparten la misma secuencia de CommandID.
    private static int _commandCounter = Random.Shared.Next(0, 90000);

    public AdmsCommandService(IServiceScopeFactory scopeFactory, ILogger<AdmsCommandService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _ = Task.Run(ExpireStaleSentCommandsAsync);
    }

    /// <summary>
    /// Al arrancar: los comandos entregados hace más de <see cref="SentCommandTtl"/> sin acuse ya no se esperan. No se
    /// reenvían (pueden ser destructivos y el reloj quizá sí los ejecutó); los pendientes siguen en la tabla.
    /// </summary>
    internal async Task ExpireStaleSentCommandsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

            var staleThreshold = DateTime.UtcNow - SentCommandTtl;
            var stale = await dbContext.AdmsCommands
                .Where(c => c.Status == AdmsCommandStatus.Sent && c.SentAtUtc < staleThreshold)
                .ToListAsync();
            foreach (var cmd in stale) cmd.MarkAsExpired();
            if (stale.Count > 0)
            {
                await dbContext.SaveChangesAsync();
                _logger.LogInformation("{Count} comandos ADMS entregados sin acuse quedaron expirados.", stale.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Advertencia expirando comandos ADMS sin acuse en el arranque");
        }
    }

    private static string Clean(string serialNumber) => serialNumber.Trim().ToUpperInvariant();

    public async Task<Guid> EnqueueCommandAsync(string serialNumber, string command, Guid? downloadLogId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("El número de serie es requerido", nameof(serialNumber));

        // El CommandID se asigna al entregarlo al reloj.
        var admsCommand = AdmsCommand.Create(Clean(serialNumber), "0", command, downloadLogId);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await dbContext.AdmsCommands.AddAsync(admsCommand, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return admsCommand.Id;
    }

    public async Task<(string? CommandId, string? Command, Guid? DownloadLogId)> GetNextCommandAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return (null, null, null);
        var cleanSn = Clean(serialNumber);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

        // Un CommandID que no tenga ya otro comando del mismo reloj entregado y sin acuse.
        var inFlight = await dbContext.AdmsCommands
            .Where(c => c.SerialNumber == cleanSn && c.Status == AdmsCommandStatus.Sent)
            .Select(c => c.CommandId)
            .ToListAsync(cancellationToken);
        string commandId;
        do
        {
            commandId = ((Interlocked.Increment(ref _commandCounter) & int.MaxValue) % 90000 + 10000).ToString();
        } while (inFlight.Contains(commandId));

        AdmsCommand? claimed;
        if (dbContext.Database.IsNpgsql())
        {
            var id = await ClaimNextAsync(dbContext, cleanSn, commandId, cancellationToken);
            claimed = id == null ? null : await dbContext.AdmsCommands.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }
        else
        {
            // Sin PostgreSQL (pruebas en memoria) no hay otros procesos.
            claimed = await dbContext.AdmsCommands
                .Where(c => c.SerialNumber == cleanSn && c.Status == AdmsCommandStatus.Pending)
                .OrderBy(c => c.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (claimed != null)
            {
                claimed.MarkAsSent(commandId);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (claimed == null) return (null, null, null);

        if (claimed.DownloadLogId.HasValue)
        {
            RegisterPendingExecution(cleanSn, commandId, claimed.DownloadLogId.Value);
        }

        return (commandId, claimed.CommandText, claimed.DownloadLogId);
    }

    /// <summary>Marca como entregado el pendiente más antiguo del reloj en una sola sentencia; null si no hay.</summary>
    private static async Task<Guid?> ClaimNextAsync(AttendanceDbContext dbContext, string serialNumber, string commandId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await dbContext.Database.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE ""AdmsCommands"" a
   SET ""Status"" = @sent, ""SentAtUtc"" = now(), ""CommandId"" = @commandId
  FROM (SELECT ""Id"" FROM ""AdmsCommands""
         WHERE ""SerialNumber"" = @sn AND ""Status"" = @pending
         ORDER BY ""CreatedAtUtc"", ""Id""
         LIMIT 1
         FOR UPDATE SKIP LOCKED) c
 WHERE a.""Id"" = c.""Id""
RETURNING a.""Id""";
        AddParameter(command, "sent", (int)AdmsCommandStatus.Sent);
        AddParameter(command, "pending", (int)AdmsCommandStatus.Pending);
        AddParameter(command, "sn", serialNumber);
        AddParameter(command, "commandId", commandId);

        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task<bool> HasPendingCommandsAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return false;
        var cleanSn = Clean(serialNumber);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        return await dbContext.AdmsCommands.AnyAsync(c => c.SerialNumber == cleanSn && c.Status == AdmsCommandStatus.Pending, cancellationToken);
    }

    public void RegisterPendingExecution(string serialNumber, string commandId, Guid downloadLogId)
    {
        var cleanSn = Clean(serialNumber);
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
        var cleanSn = Clean(serialNumber);

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
        var cleanSn = Clean(serialNumber);
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
        var cleanSn = Clean(serialNumber);
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
        var cleanSn = Clean(serialNumber);
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
        var cleanSn = Clean(serialNumber);
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
                        // Vuelve a pendiente: el reloj lo recibe en su siguiente petición, la atienda el proceso que la atienda.
                        dbCmd.ResetForRetry();
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

    public async Task<DeviceInfoDto?> WaitForDeviceInfoAsync(Guid infoCommandId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var signal = _deviceInfoSignals.GetOrAdd(infoCommandId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            while (true)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
                    var payload = await dbContext.AdmsCommands.AsNoTracking()
                        .Where(c => c.Id == infoCommandId)
                        .Select(c => c.ResultPayload)
                        .FirstOrDefaultAsync(cts.Token);
                    if (payload != null) return JsonSerializer.Deserialize<DeviceInfoDto>(payload);
                }

                await Task.WhenAny(signal.Task, Task.Delay(DeviceInfoPollInterval, cts.Token));
                cts.Token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _deviceInfoSignals.TryRemove(infoCommandId, out _);
        }
    }

    public async Task NotifyDeviceInfoReceivedAsync(string serialNumber, DeviceInfoDto deviceInfo, string? commandId = null, CancellationToken cancellationToken = default)
    {
        // Sin CommandID son opciones que el reloj envía por su cuenta: no contestan ningún INFO.
        if (string.IsNullOrWhiteSpace(serialNumber) || string.IsNullOrEmpty(commandId)) return;
        var cleanSn = Clean(serialNumber);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var infoCommand = await dbContext.AdmsCommands
            .Where(c => c.SerialNumber == cleanSn && c.CommandId == commandId && c.CommandText == "INFO" &&
                        (c.Status == AdmsCommandStatus.Sent || c.Status == AdmsCommandStatus.Confirmed))
            .OrderByDescending(c => c.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (infoCommand == null || infoCommand.ResultPayload != null) return;

        infoCommand.RecordResult(JsonSerializer.Serialize(deviceInfo));
        await dbContext.SaveChangesAsync(cancellationToken);

        if (_deviceInfoSignals.TryGetValue(infoCommand.Id, out var signal)) signal.TrySetResult();
    }
}
