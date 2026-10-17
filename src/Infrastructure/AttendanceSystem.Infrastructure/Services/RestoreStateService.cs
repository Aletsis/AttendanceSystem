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
/// </summary>
public class RestoreStateService : IRestoreStateService
{
    private readonly ILogger<RestoreStateService> _logger;
    private readonly string? _connectionString;
    private readonly object _lock = new();

    private volatile MaintenanceState _currentState = MaintenanceState.Normal;
    private volatile string? _currentOwner;
    private int _activeWritersCount;

    public RestoreStateService(
        ILogger<RestoreStateService> logger,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _connectionString = configuration?.GetConnectionString("AttendanceDb");
    }

    public bool IsRestoreInProgress => _currentState != MaintenanceState.Normal;

    public MaintenanceState CurrentState => _currentState;

    public string? CurrentOwner => _currentOwner;

    public bool CanWrite => _currentState == MaintenanceState.Normal;

    public int ActiveWritersCount => Volatile.Read(ref _activeWritersCount);

    public async Task<bool> TryAcquireRestoreLockAsync(string ownerId, string description, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_currentState != MaintenanceState.Normal)
            {
                _logger.LogWarning(
                    "Rechazando intento de restauración por {AttemptOwner}: El sistema ya está en mantenimiento ({State}) por {CurrentOwner}.",
                    ownerId,
                    _currentState,
                    _currentOwner ?? "desconocido");
                return false;
            }

            _currentOwner = ownerId;
            _currentState = MaintenanceState.Preparing;
        }

        _logger.LogWarning("🚧 Bloqueo de mantenimiento adquirido por {Owner}: {Description}", ownerId, description);

        // Persistir en esquema protegido de PostgreSQL si está disponible
        await TryPersistStateToDatabaseAsync(_currentState, ownerId, description, cancellationToken);
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

        await TryPersistStateToDatabaseAsync(targetState, _currentOwner, success ? "Operación normal reanudada" : "Fallo crítico en restauración", cancellationToken);
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
            throw new DatabaseMaintenanceException(_currentState, _currentOwner);
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

    private void DecrementWriter()
    {
        Interlocked.Decrement(ref _activeWritersCount);
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
                INSERT INTO system_maintenance.maintenance_lock (id, state, owner_id, updated_at, details)
                VALUES (1, @state, @owner, NOW(), @details)
                ON CONFLICT (id) DO UPDATE SET
                    state = EXCLUDED.state,
                    owner_id = EXCLUDED.owner_id,
                    updated_at = EXCLUDED.updated_at,
                    details = EXCLUDED.details;";

            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("state", state.ToString());
            cmd.Parameters.AddWithValue("owner", (object?)owner ?? DBNull.Value);
            cmd.Parameters.AddWithValue("details", (object?)details ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Aviso: No se pudo sincronizar el estado de mantenimiento con PostgreSQL (esto es normal durante pg_restore o desconexión).");
        }
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
