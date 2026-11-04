using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Configuration.Services;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public enum DatabaseStartupStatus
{
    /// <summary>Esquema al día y fuera de mantenimiento.</summary>
    Ready,
    /// <summary>Restauración con fallo crítico o migración fallida: tareas en pausa hasta restaurar un respaldo.</summary>
    Maintenance,
    /// <summary>Otro proceso está restaurando la base.</summary>
    RestoreInProgress,
    /// <summary>Esquema atrasado y este proceso no lo administra: tareas en pausa.</summary>
    SchemaOutdated
}

/// <summary>
/// Arranque común de los procesos que ejecutan las tareas del sistema: el sitio web y el cliente WPF (junto al sitio o
/// sin él). Se llama antes de iniciar los servicios en segundo plano: una pausa aquí impide que Hangfire arranque.
/// </summary>
public static class ServerStartup
{
    /// <summary>
    /// Deja la base lista para trabajar y devuelve en qué estado quedó. Tras una restauración con fallo crítico la base puede tener
    /// datos sin validar: migrarla o sembrarla ocultaría el problema, así que se arranca en mantenimiento (Hangfire en
    /// pausa) para que el administrador restaure un respaldo.
    /// </summary>
    /// <param name="applyMigrations">
    /// El que administra el esquema (el sitio web, o el WPF instalado sin sitio web) aplica las migraciones y los datos
    /// iniciales. El que no (el WPF junto al sitio web) sólo comprueba que el esquema esté al día: con uno atrasado sus
    /// tareas fallarían, así que quedan en pausa hasta que el sitio lo actualice y se vuelva a abrir.
    /// </param>
    public static async Task<DatabaseStartupStatus> PrepareDatabaseAsync(IServiceProvider services, bool applyMigrations, ILogger logger, CancellationToken cancellationToken = default)
    {
        var jobs = services.GetRequiredService<IBackgroundJobControlService>();
        var state = await services.GetRequiredService<IRestoreStateService>().LoadPersistedStateAsync(cancellationToken);
        if (state == MaintenanceState.Failed)
        {
            logger.LogCritical("Restauración con fallo crítico pendiente: se omiten migraciones y datos iniciales, y las tareas quedan en pausa hasta restaurar un respaldo.");
            await jobs.PauseBackgroundJobsAsync(cancellationToken);
            return DatabaseStartupStatus.Maintenance;
        }
        if (state != MaintenanceState.Normal)
        {
            // La restauración en curso deja el esquema al día al terminar; migrar a la vez chocaría con pg_restore.
            logger.LogWarning("Otro proceso está restaurando la base ({State}): se omiten migraciones y datos iniciales.", state);
            return DatabaseStartupStatus.RestoreInProgress;
        }

        if (!applyMigrations)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0) return DatabaseStartupStatus.Ready;

            logger.LogCritical("El esquema de la base está atrasado ({Count} migraciones pendientes, la última {Last}); lo actualiza el sitio web al iniciar. Las tareas quedan en pausa.",
                pending.Count, pending[^1]);
            await jobs.PauseBackgroundJobsAsync(cancellationToken);
            return DatabaseStartupStatus.SchemaOutdated;
        }

        // Respaldo previo, bloqueo entre procesos y, si algo falla, arranque en mantenimiento (ver StartupMigrationService).
        StartupMigrationResult migration;
        using (var scope = services.CreateScope())
        {
            migration = await scope.ServiceProvider.GetRequiredService<StartupMigrationService>().RunAsync(cancellationToken);
        }

        if (migration.Outcome == StartupMigrationOutcome.Maintenance)
        {
            logger.LogCritical("El esquema no quedó al día: se omiten los datos iniciales y las tareas quedan en pausa. {Reason}", migration.Message);
            await jobs.PauseBackgroundJobsAsync(cancellationToken);
            return DatabaseStartupStatus.Maintenance;
        }

        await IdentityDataSeeder.SeedAsync(services, services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityDataSeeder"));
        return DatabaseStartupStatus.Ready;
    }

    /// <summary>
    /// Programa las tareas recurrentes. Varios procesos pueden hacerlo: comparten el almacenamiento de Hangfire y
    /// AddOrUpdate sobre el mismo identificador deja una sola programación.
    /// </summary>
    public static async Task ScheduleRecurringJobsAsync(IServiceProvider services, bool databaseReady, ILogger logger, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IAttendanceJobScheduler>();
        scheduler.ScheduleCriticalAbsenceCheck();
        scheduler.ScheduleDeviceHeartbeat();
        scheduler.ScheduleCloudSync();
        scheduler.ScheduleScheduleIntegrityCheck();

        // Las de horario configurable, desde la configuración guardada: antes dependían de lo que Hangfire tuviera
        // guardado, que una restauración o un esquema nuevo de Hangfire dejaban distinto. En mantenimiento se conserva
        // la programación que haya; se aplica al reanudar las tareas tras restaurar.
        if (!databaseReady) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobsSynchronizer>().ApplyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudieron programar las tareas de horario configurable al iniciar; se conserva la programación anterior.");
        }
    }
}
