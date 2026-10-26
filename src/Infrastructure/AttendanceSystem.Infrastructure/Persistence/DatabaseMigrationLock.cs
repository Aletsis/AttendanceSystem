using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Persistence;

/// <summary>
/// Bloqueo consultivo de sesión que serializa la aplicación de migraciones entre procesos (varios workers de IIS,
/// un reciclado solapado o una restauración). El bloqueo que toma EF sobre <c>__EFMigrationsHistory</c> dura una sola
/// transacción, así que dos procesos que arrancan a la vez intentan crear las mismas tablas y uno de ellos falla.
/// </summary>
internal sealed class DatabaseMigrationLock : IAsyncDisposable
{
    public const string LockApplicationName = "attendance_migration_lock";

    private const long AdvisoryLockKey = 7235002;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly NpgsqlConnection _connection;

    private DatabaseMigrationLock(NpgsqlConnection connection) => _connection = connection;

    /// <summary>
    /// Espera a que ningún otro proceso esté migrando y toma el bloqueo. Quien lo obtiene después de esperar debe
    /// volver a consultar las migraciones pendientes: el otro proceso pudo haberlas aplicado ya.
    /// </summary>
    public static async Task<DatabaseMigrationLock> AcquireAsync(string connectionString, TimeSpan timeout, ILogger logger, CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = LockApplicationName,
            // Si el proceso muere, PostgreSQL detecta la conexión caída y libera el bloqueo.
            KeepAlive = 15
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            var deadline = DateTime.UtcNow + timeout;
            var waiting = false;

            while (true)
            {
                await using (var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection))
                {
                    cmd.Parameters.AddWithValue("key", AdvisoryLockKey);
                    if ((bool)(await cmd.ExecuteScalarAsync(cancellationToken) ?? false))
                    {
                        if (waiting) logger.LogInformation("El otro proceso terminó de migrar; se continúa.");
                        return new DatabaseMigrationLock(connection);
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"Otro proceso lleva más de {timeout.TotalMinutes:0} minutos aplicando migraciones.");
                }

                if (!waiting)
                {
                    logger.LogWarning("Otro proceso está aplicando migraciones a la base de datos; se espera a que termine...");
                    waiting = true;
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Cerrar la sesión libera el bloqueo consultivo.</summary>
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
