using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Implementación Singleton y Thread-Safe de IRestoreStateService con máquina de estados,
/// control de propietarios, admisión y drenaje de escritores, y coordinación en PostgreSQL.
/// <para>
/// Entre procesos (servidor web, clientes WPF, varios workers de IIS) la coordinación usa dos piezas en PostgreSQL:
/// un bloqueo consultivo de sesión que el proceso que restaura conserva mientras dura la operación (exclusión mutua
/// y prueba de vida) y la fila de <c>system_maintenance.maintenance_lock</c>, que los demás procesos consultan
/// periódicamente para dejar de escribir mientras otro restaura o tras un fallo crítico.
/// </para>
/// </summary>
public class RestoreStateService : IRestoreStateService, IDisposable
{
    /// <summary>Nombre de la conexión que retiene el bloqueo: la restauración no debe terminarla.</summary>
    public const string LockApplicationName = "attendance_maintenance_lock";

    /// <summary>Propietario del mantenimiento que deja un arranque que no pudo poner el esquema al día.</summary>
    public const string StartupMaintenanceOwner = "StartupMigration";

    private const long AdvisoryLockKey = 7235001;

    private readonly ILogger<RestoreStateService> _logger;
    private readonly string? _connectionString;
    private readonly object _lock = new();
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly Timer? _monitor;
    private int _refreshing;

    private volatile MaintenanceState _currentState = MaintenanceState.Normal;
    private volatile string? _currentOwner;
    private volatile MaintenanceState _remoteState = MaintenanceState.Normal;
    private volatile string? _remoteOwner;
    private NpgsqlConnection? _lockConnection;
    private int _activeWritersCount;

    public RestoreStateService(
        ILogger<RestoreStateService> logger,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _connectionString = configuration?.GetConnectionString("AttendanceDb");

        var pollSeconds = configuration?.GetValue<int>("Maintenance:PollIntervalSeconds", 5) ?? 0;
        if (!string.IsNullOrWhiteSpace(_connectionString) && pollSeconds > 0)
        {
            var interval = TimeSpan.FromSeconds(pollSeconds);
            _monitor = new Timer(_ => _ = RefreshFromMonitorAsync(), null, interval, interval);
        }
    }

    /// <summary>Estado efectivo: el propio si este proceso está en mantenimiento; si no, el de otro proceso.</summary>
    public MaintenanceState CurrentState => _currentState != MaintenanceState.Normal ? _currentState : _remoteState;

    public bool IsRestoreInProgress => CurrentState != MaintenanceState.Normal;

    public string? CurrentOwner => _currentState != MaintenanceState.Normal ? _currentOwner : _remoteOwner;

    public bool CanWrite => CurrentState == MaintenanceState.Normal;

    public int ActiveWritersCount => Volatile.Read(ref _activeWritersCount);

    public async Task<MaintenanceState> LoadPersistedStateAsync(CancellationToken cancellationToken = default)
    {
        var row = await ReadPersistedStateAsync(cancellationToken);
        if (row == null || row.State == MaintenanceState.Normal)
        {
            return CurrentState;
        }

        if (row.State == MaintenanceState.Failed && row.Owner == StartupMaintenanceOwner)
        {
            // Lo dejó un arranque anterior y la base no se tocó después del fallo: este arranque vuelve a intentarlo.
            _logger.LogWarning("El arranque anterior dejó el sistema en mantenimiento porque no pudo actualizar el esquema; se vuelve a intentar.");
            await TryPersistStateToDatabaseAsync(MaintenanceState.Normal, null, "Mantenimiento del arranque anterior descartado para reintentar", cancellationToken);
            return CurrentState;
        }

        if (row.LockHeld && row.State != MaintenanceState.Failed)
        {
            // Otro proceso está restaurando ahora mismo: no es un resto de este proceso.
            ApplyRemoteState(row.State, row.Owner);
            _logger.LogWarning("Otro proceso tiene una restauración en curso ({State}, {Owner}). Se bloquean las escrituras hasta que termine.",
                row.State, row.Owner ?? "desconocido");
            return CurrentState;
        }

        // Validating y Recovering ocurren después de un COMMIT: la base puede tener datos restaurados sin validar.
        if (row.State is MaintenanceState.Failed or MaintenanceState.Validating or MaintenanceState.Recovering)
        {
            lock (_lock)
            {
                _currentState = MaintenanceState.Failed;
                _currentOwner = row.Owner;
            }

            _logger.LogCritical(
                "🚨 Se encontró una restauración con fallo crítico ({PersistedState}, {Owner}). El sistema arranca en mantenimiento (Failed): " +
                "restaure un respaldo desde la página de respaldos para salir de este estado.",
                row.State,
                row.Owner ?? "desconocido");

            if (row.State != MaintenanceState.Failed)
            {
                await TryPersistStateToDatabaseAsync(MaintenanceState.Failed, row.Owner, $"Restauración interrumpida en {row.State}", cancellationToken);
            }
            return MaintenanceState.Failed;
        }

        _logger.LogWarning(
            "Se encontró una restauración interrumpida en {PersistedState} ({Owner}). No llegó a confirmarse, así que la base no se modificó; se reanuda la operación normal.",
            row.State,
            row.Owner ?? "desconocido");
        await TryPersistStateToDatabaseAsync(MaintenanceState.Normal, null, $"Restauración interrumpida en {row.State} descartada al arrancar", cancellationToken);
        return CurrentState;
    }

    public async Task EnterStartupMaintenanceAsync(string reason, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _currentState = MaintenanceState.Failed;
            _currentOwner = StartupMaintenanceOwner;
        }

        _logger.LogCritical("🚨 El sistema arranca en mantenimiento (Failed): {Reason} Restaure un respaldo desde la página de respaldos, " +
            "o corrija la causa y reinicie la aplicación para volver a intentarlo.", reason);
        await TryPersistStateToDatabaseAsync(MaintenanceState.Failed, StartupMaintenanceOwner, reason, cancellationToken);
    }

    public async Task<bool> TryAcquireRestoreLockAsync(string ownerId, string description, CancellationToken cancellationToken = default)
    {
        MaintenanceState previousState;
        string? previousOwner;

        lock (_lock)
        {
            var remoteInProgress = _remoteState is not (MaintenanceState.Normal or MaintenanceState.Failed);
            if ((_currentState != MaintenanceState.Normal && _currentState != MaintenanceState.Failed) || remoteInProgress)
            {
                _logger.LogWarning(
                    "Rechazando intento de restauración por {AttemptOwner}: El sistema ya está en mantenimiento ({State}) por {CurrentOwner}.",
                    ownerId,
                    CurrentState,
                    CurrentOwner ?? "desconocido");
                return false;
            }

            previousState = _currentState;
            previousOwner = _currentOwner;
            _currentOwner = ownerId;
            _currentState = MaintenanceState.Preparing;
        }

        // Exclusión entre procesos: sólo uno puede retener el bloqueo consultivo.
        if (!await TryTakeAdvisoryLockAsync(cancellationToken))
        {
            lock (_lock)
            {
                _currentState = previousState;
                _currentOwner = previousOwner;
            }

            _logger.LogWarning("Rechazando intento de restauración por {AttemptOwner}: otro proceso tiene una restauración en curso.", ownerId);
            await RefreshRemoteStateAsync(cancellationToken);
            return false;
        }

        _logger.LogWarning("🚧 Bloqueo de mantenimiento adquirido por {Owner}: {Description}", ownerId, description);

        // Persistir en esquema protegido de PostgreSQL si está disponible
        await TryPersistStateToDatabaseAsync(MaintenanceState.Preparing, ownerId, description, cancellationToken);
        return true;
    }

    public async Task ReleaseRestoreLockAsync(string ownerId, bool success, CancellationToken cancellationToken = default)
    {
        var targetState = success ? MaintenanceState.Normal : MaintenanceState.Failed;

        lock (_lock)
        {
            if (_currentOwner != null && _currentOwner != ownerId)
            {
                _logger.LogWarning(
                    "Aviso: El propietario {Caller} intentó liberar el bloqueo adquirido por {Owner}.",
                    ownerId,
                    _currentOwner);
            }

            _currentState = targetState;
            if (success)
            {
                _currentOwner = null;
                // Una restauración exitosa también resuelve un Failed que hubiera dejado otro proceso.
                _remoteState = MaintenanceState.Normal;
                _remoteOwner = null;
            }
        }

        if (success)
        {
            _logger.LogInformation("✅ Modo de restauración desactivado exitosamente. Retornando a estado Normal.");
        }
        else
        {
            _logger.LogCritical("🚨 Restauración finalizada con fallo crítico. El sistema permanece en mantenimiento (Failed).");
        }

        // Primero el estado final y después el bloqueo: los demás nunca ven "en curso" sin dueño vivo.
        await TryPersistStateToDatabaseAsync(targetState, _currentOwner, success ? "Operación normal reanudada" : "Fallo crítico en restauración", cancellationToken);
        await ReleaseAdvisoryLockAsync();
    }

    public async Task TransitionStateAsync(MaintenanceState newState, string? details = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _currentState = newState;
        }

        _logger.LogInformation("Transición de estado de mantenimiento -> {NewState}. Detalle: {Details}", newState, details ?? "Sin detalles");
        await TryPersistStateToDatabaseAsync(newState, _currentOwner, details, cancellationToken);
    }

    public IDisposable RegisterActiveWriter()
    {
        if (!CanWrite)
        {
            throw new DatabaseMaintenanceException(CurrentState, CurrentOwner);
        }

        Interlocked.Increment(ref _activeWritersCount);
        return new ActiveWriterToken(this);
    }

    public async Task DrainActiveWritersAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando drenaje de escritores activos. Escritores actuales: {Count}", ActiveWritersCount);

        var start = DateTime.UtcNow;
        while (ActiveWritersCount > 0)
        {
            if (DateTime.UtcNow - start > timeout)
            {
                _logger.LogWarning("Tiempo límite alcanzado para drenar escritores activos ({Count} escritores aún en vuelo).", ActiveWritersCount);
                break;
            }

            await Task.Delay(50, cancellationToken);
        }

        _logger.LogInformation("Drenaje de escritores concluido. Escritores restantes: {Count}", ActiveWritersCount);
    }

    public void EnterRestoreMode()
    {
        lock (_lock)
        {
            _currentOwner = "LegacyCaller";
            _currentState = MaintenanceState.Restoring;
        }
        _logger.LogWarning("🚧 Modo de restauración activado (EnterRestoreMode).");
    }

    public void ExitRestoreMode()
    {
        lock (_lock)
        {
            _currentState = MaintenanceState.Normal;
            _currentOwner = null;
        }
        _logger.LogInformation("✅ Modo de restauración desactivado (ExitRestoreMode).");
    }

    /// <summary>
    /// Lee el estado que dejó otro proceso. Una fila "en curso" sin nadie que retenga el bloqueo consultivo es un resto
    /// de un proceso que murió: antes del COMMIT se ignora (la base no cambió); después, cuenta como Failed.
    /// </summary>
    internal async Task RefreshRemoteStateAsync(CancellationToken cancellationToken = default)
    {
        var row = await ReadPersistedStateAsync(cancellationToken);
        if (row == null)
        {
            // Sin lectura (p. ej. conexión terminada por una restauración) se conserva el último estado conocido.
            return;
        }

        if (row.InstanceId == _instanceId || row.State == MaintenanceState.Normal)
        {
            ApplyRemoteState(MaintenanceState.Normal, null);
        }
        else if (row.State == MaintenanceState.Failed || row.LockHeld)
        {
            ApplyRemoteState(row.State, row.Owner);
        }
        else if (row.State is MaintenanceState.Validating or MaintenanceState.Recovering)
        {
            ApplyRemoteState(MaintenanceState.Failed, row.Owner);
        }
        else
        {
            ApplyRemoteState(MaintenanceState.Normal, null);
        }
    }

    private async Task RefreshFromMonitorAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            await RefreshRemoteStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo refrescar el estado de mantenimiento de otros procesos.");
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private void ApplyRemoteState(MaintenanceState state, string? owner)
    {
        var previous = _remoteState;
        lock (_lock)
        {
            _remoteState = state;
            _remoteOwner = owner;
        }

        if (previous != state)
        {
            if (state == MaintenanceState.Normal)
            {
                _logger.LogInformation("✅ Otro proceso terminó su mantenimiento; se reanudan las escrituras.");
            }
            else
            {
                _logger.LogWarning("🚧 Otro proceso tiene la base en mantenimiento ({State}, {Owner}): se bloquean las escrituras.", state, owner ?? "desconocido");
            }
        }
    }

    private sealed record PersistedState(MaintenanceState State, string? Owner, string? InstanceId, bool LockHeld);

    private async Task<PersistedState?> ReadPersistedStateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return null;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString) { Pooling = false, Timeout = 5, CommandTimeout = 5 };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var existsCmd = new NpgsqlCommand("SELECT to_regclass('system_maintenance.maintenance_lock') IS NOT NULL", connection);
            if (!(bool)(await existsCmd.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                return new PersistedState(MaintenanceState.Normal, null, null, false);
            }

            // Un bloqueo consultivo de clave bigint aparece en pg_locks con classid = parte alta y objid = parte baja.
            const string sql = @"
                SELECT m.state, m.owner_id, m.instance_id,
                       EXISTS (SELECT 1 FROM pg_locks l
                               WHERE l.locktype = 'advisory' AND l.granted
                                 AND l.database = (SELECT oid FROM pg_database WHERE datname = current_database())
                                 AND l.classid::bigint = (@key >> 32) AND l.objid::bigint = (@key & 4294967295) AND l.objsubid = 1)
                FROM system_maintenance.maintenance_lock m
                WHERE m.id = 1";
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("key", AdvisoryLockKey);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new PersistedState(MaintenanceState.Normal, null, null, false);
            }

            var state = Enum.TryParse<MaintenanceState>(reader.GetString(0), out var parsed) ? parsed : MaintenanceState.Normal;
            return new PersistedState(
                state,
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetBoolean(3));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer el estado de mantenimiento persistido.");
            return null;
        }
    }

    private async Task<bool> TryTakeAdvisoryLockAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return true;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Pooling = false,
                Timeout = 15,
                CommandTimeout = 15,
                ApplicationName = LockApplicationName,
                // Si el proceso muere, PostgreSQL detecta la conexión caída y libera el bloqueo.
                KeepAlive = 15
            };
            var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            cmd.Parameters.AddWithValue("key", AdvisoryLockKey);
            if ((bool)(await cmd.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                _lockConnection = connection;
                return true;
            }

            await connection.DisposeAsync();
            return false;
        }
        catch (Exception ex)
        {
            // Sin poder verificar el bloqueo no se sabe si otro proceso restaura: no se arriesga una restauración simultánea.
            _logger.LogWarning(ex, "No se pudo tomar el bloqueo de mantenimiento en PostgreSQL; se rechaza la restauración.");
            return false;
        }
    }

    private async Task ReleaseAdvisoryLockAsync()
    {
        var connection = Interlocked.Exchange(ref _lockConnection, null);
        if (connection == null) return;

        try
        {
            // Cerrar la sesión libera el bloqueo consultivo.
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Aviso al liberar el bloqueo de mantenimiento.");
        }
    }

    private async Task TryPersistStateToDatabaseAsync(MaintenanceState state, string? owner, string? details, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Timeout = 3,
                CommandTimeout = 5
            };

            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                CREATE SCHEMA IF NOT EXISTS system_maintenance;
                CREATE TABLE IF NOT EXISTS system_maintenance.maintenance_lock (
                    id INT PRIMARY KEY,
                    state VARCHAR(50) NOT NULL,
                    owner_id VARCHAR(100),
                    updated_at TIMESTAMPTZ NOT NULL,
                    details TEXT
                );
                ALTER TABLE system_maintenance.maintenance_lock ADD COLUMN IF NOT EXISTS instance_id VARCHAR(64);
                INSERT INTO system_maintenance.maintenance_lock (id, state, owner_id, updated_at, details, instance_id)
                VALUES (1, @state, @owner, NOW(), @details, @instance)
                ON CONFLICT (id) DO UPDATE SET
                    state = EXCLUDED.state,
                    owner_id = EXCLUDED.owner_id,
                    updated_at = EXCLUDED.updated_at,
                    details = EXCLUDED.details,
                    instance_id = EXCLUDED.instance_id;";

            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("state", state.ToString());
            cmd.Parameters.AddWithValue("owner", (object?)owner ?? DBNull.Value);
            cmd.Parameters.AddWithValue("details", (object?)details ?? DBNull.Value);
            cmd.Parameters.AddWithValue("instance", _instanceId);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Aviso: No se pudo sincronizar el estado de mantenimiento con PostgreSQL (esto es normal durante pg_restore o desconexión).");
        }
    }

    private void DecrementWriter()
    {
        Interlocked.Decrement(ref _activeWritersCount);
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        _lockConnection?.Dispose();
        _lockConnection = null;
        GC.SuppressFinalize(this);
    }

    private sealed class ActiveWriterToken : IDisposable
    {
        private readonly RestoreStateService _service;
        private int _disposed;

        public ActiveWriterToken(RestoreStateService service)
        {
            _service = service;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _service.DecrementWriter();
            }
        }
    }
}
