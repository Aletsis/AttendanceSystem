using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

public enum StartupMigrationOutcome
{
    /// <summary>La base ya estaba al día.</summary>
    UpToDate,

    /// <summary>Se aplicaron las migraciones pendientes.</summary>
    Migrated,

    /// <summary>El esquema no quedó al día y el sistema quedó en mantenimiento (Failed).</summary>
    Maintenance
}

public sealed record StartupMigrationResult(StartupMigrationOutcome Outcome, string Message);

/// <summary>
/// Aplica al arrancar las migraciones pendientes del servidor web, de a un proceso a la vez y después de respaldar la
/// base: varias migraciones transforman o eliminan datos y algunas no tienen vuelta atrás.
/// <para>
/// Si el respaldo previo falla, si una migración falla o si la base es de una versión más nueva, la aplicación arranca
/// en mantenimiento en vez de detenerse: así el administrador puede entrar a restaurar un respaldo. Sólo se detiene si
/// no consigue conectar con PostgreSQL, porque sin base no hay nada que mostrar y el siguiente arranque lo reintenta.
/// </para>
/// </summary>
public class StartupMigrationService
{
    private readonly IDbContextFactory<AttendanceDbContext> _contextFactory;
    private readonly IBackupService _backupService;
    private readonly IRestoreStateService _restoreState;
    private readonly ILogger<StartupMigrationService> _logger;
    private readonly TimeSpan _migrationTimeout;
    private readonly int _connectAttempts;
    private readonly TimeSpan _connectRetryDelay;

    public StartupMigrationService(
        IDbContextFactory<AttendanceDbContext> contextFactory,
        IBackupService backupService,
        IRestoreStateService restoreState,
        ILogger<StartupMigrationService> logger,
        IConfiguration configuration)
    {
        _contextFactory = contextFactory;
        _backupService = backupService;
        _restoreState = restoreState;
        _logger = logger;
        _migrationTimeout = MigrationTimeout(configuration);
        _connectAttempts = Math.Max(configuration.GetValue("Database:StartupConnectAttempts", 6), 1);
        _connectRetryDelay = TimeSpan.FromSeconds(Math.Max(configuration.GetValue("Database:StartupConnectRetrySeconds", 2), 0));
    }

    /// <summary>
    /// Tiempo máximo de cada comando de una migración y de la espera a que otro proceso termine de migrar. Las
    /// migraciones de datos sobre tablas grandes superan con facilidad los 30 segundos por defecto de Npgsql.
    /// </summary>
    internal static TimeSpan MigrationTimeout(IConfiguration configuration) =>
        TimeSpan.FromMinutes(Math.Max(configuration.GetValue("Database:MigrationTimeoutMinutes", 30), 1));

    public async Task<StartupMigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureDatabaseReachableAsync(context, cancellationToken);

        string? backupPath = null;
        try
        {
            await using var migrationLock = await DatabaseMigrationLock.AcquireAsync(
                context.Database.GetConnectionString()!, _migrationTimeout, _logger, cancellationToken);

            // Se consulta con el bloqueo tomado: si otro proceso migró mientras se esperaba, ya no queda nada pendiente.
            var known = context.Database.GetMigrations().ToList();
            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

            var unknown = RetiredMigrations.Unknown(applied, known);
            if (unknown.Count > 0)
            {
                // Pasa al reinstalar una versión anterior: este código no sabe trabajar con ese esquema.
                return await EnterMaintenanceAsync(
                    $"La base de datos es de una versión más nueva del sistema (migraciones desconocidas: {string.Join(", ", unknown.Take(3))}" +
                    $"{(unknown.Count > 3 ? ", ..." : "")}). Instale esa versión o una posterior.", cancellationToken);
            }

            var pending = known.Except(applied).ToList();
            if (pending.Count == 0)
            {
                _logger.LogInformation("La base de datos está actualizada. No hay migraciones pendientes");
                return new StartupMigrationResult(StartupMigrationOutcome.UpToDate, "La base de datos está actualizada.");
            }

            _logger.LogWarning("Se encontraron {Count} migraciones pendientes. Aplicando migraciones...", pending.Count);
            foreach (var migration in pending)
            {
                _logger.LogInformation("Migración pendiente: {Migration}", migration);
            }

            // Una base recién creada no tiene nada que proteger.
            if (applied.Count > 0)
            {
                var backup = await _backupService.CreatePreMigrationBackupAsync($"{applied.Last()} → {pending.Last()}", cancellationToken);
                if (!backup.Success)
                {
                    return await EnterMaintenanceAsync(
                        $"No se aplicaron las {pending.Count} migraciones pendientes porque falló el respaldo previo: {backup.Message}. " +
                        "La base no se modificó.", cancellationToken);
                }
                backupPath = backup.BackupFilePath;
                _logger.LogInformation("Respaldo previo a las migraciones: {Path}", backupPath);
            }

            context.Database.SetCommandTimeout(_migrationTimeout);
            await context.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Todas las migraciones se aplicaron correctamente");
            return new StartupMigrationResult(StartupMigrationOutcome.Migrated, $"Se aplicaron {pending.Count} migraciones.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error al verificar o aplicar migraciones de base de datos");

            // Cada migración se confirma por separado: la base queda en la última que terminó, no a medias.
            var lastApplied = await TryGetLastAppliedMigrationAsync(cancellationToken);
            return await EnterMaintenanceAsync(
                $"Falló la actualización del esquema: {ex.Message}. La base quedó en la migración {lastApplied ?? "(desconocida)"}." +
                (backupPath != null ? $" Respaldo previo: {backupPath}." : ""), cancellationToken);
        }
    }

    /// <summary>
    /// Al reiniciar el servidor, PostgreSQL puede arrancar después que IIS: se reintenta antes de rendirse. La base se
    /// crea vacía si no existe (desarrollo); el instalador la crea con su propietario.
    /// </summary>
    private async Task EnsureDatabaseReachableAsync(AttendanceDbContext context, CancellationToken cancellationToken)
    {
        var creator = context.GetService<IRelationalDatabaseCreator>();
        var delay = _connectRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!await creator.ExistsAsync(cancellationToken))
                {
                    _logger.LogWarning("La base de datos no existe; se crea vacía para aplicarle las migraciones.");
                    await creator.CreateAsync(cancellationToken);
                }
                return;
            }
            // EF envuelve el fallo transitorio de Npgsql en una InvalidOperationException.
            catch (Exception ex) when (IsTransientConnectionFailure(ex) && attempt < _connectAttempts)
            {
                _logger.LogWarning("No se pudo conectar con PostgreSQL (intento {Attempt} de {Attempts}): {Message}. Reintentando en {Delay} s...",
                    attempt, _connectAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
            }
        }
    }

    private static bool IsTransientConnectionFailure(Exception ex) =>
        ex is NpgsqlException { IsTransient: true } || ex.InnerException is NpgsqlException { IsTransient: true };

    private async Task<string?> TryGetLastAppliedMigrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer la última migración aplicada.");
            return null;
        }
    }

    private async Task<StartupMigrationResult> EnterMaintenanceAsync(string reason, CancellationToken cancellationToken)
    {
        await _restoreState.EnterStartupMaintenanceAsync(reason, cancellationToken);
        return new StartupMigrationResult(StartupMigrationOutcome.Maintenance, reason);
    }
}
