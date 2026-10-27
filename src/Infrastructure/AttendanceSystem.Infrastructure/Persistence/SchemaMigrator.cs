using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Persistence;

/// <summary>
/// Aplica migraciones sin quedarse colgado detrás de otra sesión. Un <c>ALTER TABLE</c> necesita la tabla en
/// exclusiva: si un cliente WPF tiene una transacción abierta, o quedó la sesión de un proceso que murió migrando,
/// el <c>ALTER</c> espera indefinidamente y bloquea detrás de él toda consulta a esa tabla. Con <c>lock_timeout</c>
/// la migración falla pronto, se deshace completa, y el mensaje dice qué sesiones tienen las tablas.
/// </summary>
internal static class SchemaMigrator
{
    /// <summary>Lo que una sentencia de migración espera a que otra sesión suelte una tabla.</summary>
    internal static TimeSpan LockTimeout(IConfiguration configuration) =>
        TimeSpan.FromSeconds(Math.Max(configuration.GetValue("Database:MigrationLockTimeoutSeconds", 120), 1));

    /// <summary>
    /// Ajusta la sesión del contexto antes de abrirla: también la lectura del historial queda sujeta a
    /// <paramref name="lockTimeout"/>, porque una migración huérfana lo tiene bloqueado en exclusiva.
    /// </summary>
    public static void UseMigrationSession(AttendanceDbContext context, TimeSpan commandTimeout, TimeSpan lockTimeout)
    {
        var builder = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        builder.Options = $"{builder.Options} -c lock_timeout={(long)lockTimeout.TotalMilliseconds}".Trim();
        context.Database.SetConnectionString(builder.ConnectionString);
        context.Database.SetCommandTimeout(commandTimeout);
    }

    /// <summary>Tiempo total del arranque para respaldar y migrar; debe vencer antes que <c>startupTimeLimit</c> de IIS.</summary>
    internal static TimeSpan StartupBudget(IConfiguration configuration) =>
        TimeSpan.FromMinutes(Math.Max(configuration.GetValue("Database:StartupMigrationBudgetMinutes", 50), 1));

    /// <summary>Por debajo de esto no se empieza otra migración: es preferible reiniciar y continuar.</summary>
    private static readonly TimeSpan MinimumStepTime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Aplica las pendientes de una en una, registrando cuál se aplica y cuánto tarda. Con <paramref name="deadlineUtc"/>,
    /// ningún comando pasa de esa hora: si IIS mata el arranque, no queda registro de qué pasó y la sesión que estaba
    /// migrando sigue viva en PostgreSQL, bloqueando las tablas. Al vencer lanza <see cref="TimeoutException"/>; las
    /// migraciones ya aplicadas quedan y el siguiente arranque continúa con las demás.
    /// </summary>
    public static async Task ApplyAsync(AttendanceDbContext context, IReadOnlyList<string> applied, IReadOnlyList<string> pending,
        TimeSpan commandTimeout, DateTime? deadlineUtc, ILogger logger, CancellationToken cancellationToken)
    {
        // Migrar hasta una migración revierte las aplicadas posteriores: si alguna pendiente es anterior a la última
        // aplicada (un ID retirado), se aplican todas juntas.
        var lastApplied = applied.Count > 0 ? applied.Max(StringComparer.Ordinal) : null;
        if (lastApplied != null && pending.Any(p => string.CompareOrdinal(p, lastApplied) < 0))
        {
            logger.LogInformation("Aplicando las {Count} migraciones juntas: hay pendientes anteriores a la última aplicada ({Last}).",
                pending.Count, lastApplied);
            await RunStepAsync(context, null, $"las {pending.Count} migraciones pendientes", commandTimeout, deadlineUtc, cancellationToken);
            return;
        }

        var total = Stopwatch.StartNew();
        for (var i = 0; i < pending.Count; i++)
        {
            logger.LogInformation("Aplicando migración {Index}/{Count}: {Migration}...", i + 1, pending.Count, pending[i]);
            var step = Stopwatch.StartNew();
            await RunStepAsync(context, pending[i], $"la migración {i + 1} de {pending.Count} ({pending[i]})", commandTimeout, deadlineUtc, cancellationToken);
            logger.LogInformation("Migración {Migration} aplicada en {Elapsed:0.0} s.", pending[i], step.Elapsed.TotalSeconds);
        }
        logger.LogInformation("{Count} migraciones aplicadas en {Elapsed:0.0} s.", pending.Count, total.Elapsed.TotalSeconds);
    }

    private static async Task RunStepAsync(AttendanceDbContext context, string? target, string description,
        TimeSpan commandTimeout, DateTime? deadlineUtc, CancellationToken cancellationToken)
    {
        if (deadlineUtc is { } deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining < MinimumStepTime)
            {
                throw new TimeoutException($"Se agotó el tiempo de arranque antes de {description}. Las anteriores quedaron " +
                    "aplicadas: reinicie la aplicación para continuar.");
            }
            context.Database.SetCommandTimeout(remaining < commandTimeout ? remaining : commandTimeout);
        }

        try
        {
            await context.Database.MigrateAsync(target, cancellationToken);
        }
        catch (Exception ex) when (deadlineUtc is { } expires && DateTime.UtcNow >= expires - TimeSpan.FromSeconds(5))
        {
            throw new TimeoutException($"{char.ToUpper(description[0])}{description[1..]} no terminó dentro del tiempo de " +
                "arranque y se deshizo. Las anteriores quedaron aplicadas: reinicie la aplicación para continuar, o aumente " +
                "Database:StartupMigrationBudgetMinutes si esa migración necesita más tiempo.", ex);
        }
    }

    public static bool IsLockTimeout(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }) return true;
        }
        return false;
    }

    /// <summary>
    /// Sesiones de esta base que tienen tablas tomadas, de la más antigua a la más reciente; null si no hay ninguna
    /// o no se pudo consultar. Usa su propia conexión: la del contexto puede estar en una transacción abortada.
    /// </summary>
    public static async Task<string?> DescribeSessionsHoldingTablesAsync(string connectionString, ILogger logger, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.pid,
                   coalesce(nullif(a.application_name, ''), '(sin nombre)'),
                   coalesce(host(a.client_addr), 'local'),
                   coalesce(a.state, '?'),
                   coalesce(extract(epoch FROM now() - coalesce(a.xact_start, a.query_start))::int, 0),
                   string_agg(DISTINCT c.relname, ', ' ORDER BY c.relname),
                   left(regexp_replace(coalesce(a.query, ''), '\s+', ' ', 'g'), 100)
              FROM pg_locks l
              JOIN pg_stat_activity a ON a.pid = l.pid
              JOIN pg_class c ON c.oid = l.relation
             WHERE l.granted AND l.locktype = 'relation' AND c.relkind IN ('r', 'p')
               AND a.datname = current_database() AND a.pid <> pg_backend_pid()
             GROUP BY a.pid, a.application_name, a.client_addr, a.state, a.xact_start, a.query_start, a.query
             ORDER BY 5 DESC
             LIMIT 5
            """;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 10, CommandTimeout = 10 };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var sessions = new StringBuilder();
            while (await reader.ReadAsync(cancellationToken))
            {
                if (sessions.Length > 0) sessions.Append("; ");
                sessions.Append($"pid {reader.GetInt32(0)} ({reader.GetString(1)}, {reader.GetString(2)}, {reader.GetString(3)} " +
                    $"desde hace {FormatAge(reader.GetInt32(4))}, tablas: {reader.GetString(5)}, consulta: {reader.GetString(6)})");
            }
            return sessions.Length > 0 ? sessions.ToString() : null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudieron consultar las sesiones que tienen tablas tomadas.");
            return null;
        }
    }

    /// <summary>Mensaje para una migración cancelada por <c>lock_timeout</c>.</summary>
    public static string LockTimeoutMessage(TimeSpan lockTimeout, string? sessions) =>
        $"Una sentencia esperó más de {lockTimeout.TotalSeconds:0} s a que otras sesiones soltaran tablas de la base y se " +
        "canceló sin aplicar cambios. " +
        (sessions != null ? $"Sesiones que las tienen: {sessions}. " : "") +
        "Cierre los clientes conectados (o termine esas sesiones con pg_terminate_backend) y vuelva a intentarlo.";

    private static string FormatAge(int seconds) => seconds switch
    {
        < 60 => $"{seconds} s",
        < 3600 => $"{seconds / 60} min",
        _ => $"{seconds / 3600} h {seconds % 3600 / 60} min"
    };
}
