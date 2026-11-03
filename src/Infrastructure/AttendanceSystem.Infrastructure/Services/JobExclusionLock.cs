using System.Collections.Concurrent;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Una sola ejecución a la vez de cada tarea programada, entre workers y entre procesos. En PostgreSQL usa
/// <c>pg_try_advisory_lock(espacio, clave)</c> de sesión sobre una conexión propia que se mantiene abierta mientras
/// dura la tarea: no vence aunque la tarea tarde (los candados de Hangfire sí), y se libera solo si el proceso muere.
/// Sin PostgreSQL (InMemory en pruebas) el candado es del proceso.
/// </summary>
public sealed class JobExclusionLock
{
    /// <summary>'JOBS'; distinto del espacio de los bloqueos de asistencia.</summary>
    public const int LockNamespace = 0x4A4F4253;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InProcess = new(StringComparer.Ordinal);

    private readonly AttendanceDbContext _context;

    public JobExclusionLock(AttendanceDbContext context) => _context = context;

    /// <summary>El candado tomado, o <c>null</c> si otra ejecución de la misma tarea lo tiene.</summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        if (!_context.Database.IsNpgsql())
        {
            var semaphore = InProcess.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(0, cancellationToken) ? new InProcessHold(semaphore) : null;
        }

        var key = PostgresAttendanceCalculationLockService.LockKey(name);
        var connection = new NpgsqlConnection(_context.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@ns, @key)", connection);
            command.Parameters.AddWithValue("ns", LockNamespace);
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new PostgresHold(connection, key);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class InProcessHold(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PostgresHold(NpgsqlConnection connection, int key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                // La conexión vuelve al pool: el candado de sesión se suelta antes de devolverla.
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@ns, @key)", connection);
                command.Parameters.AddWithValue("ns", LockNamespace);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            catch
            {
                // Conexión rota (p. ej. terminada por una restauración): la sesión ya no existe y el candado tampoco.
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
