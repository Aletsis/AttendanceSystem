using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public enum StartupMigrationOutcome
{
    /// <summary>La base ya estaba al día.</summary>
    UpToDate,

    /// <summary>Se aplicaron las migraciones pendientes.</summary>
    Migrated
}

public sealed record StartupMigrationResult(StartupMigrationOutcome Outcome, string Message);

/// <summary>
/// Aplica al arrancar las migraciones pendientes del servidor web, de a un proceso a la vez y después de respaldar la
/// base: varias migraciones transforman o eliminan datos y algunas no tienen vuelta atrás.
/// </summary>
public class StartupMigrationService
{
    private readonly IDbContextFactory<AttendanceDbContext> _contextFactory;
    private readonly IBackupService _backupService;
    private readonly ILogger<StartupMigrationService> _logger;
    private readonly TimeSpan _lockTimeout;

    public StartupMigrationService(
        IDbContextFactory<AttendanceDbContext> contextFactory,
        IBackupService backupService,
        ILogger<StartupMigrationService> logger,
        IConfiguration configuration)
    {
        _contextFactory = contextFactory;
        _backupService = backupService;
        _logger = logger;
        _lockTimeout = TimeSpan.FromMinutes(Math.Max(configuration.GetValue("Database:MigrationTimeoutMinutes", 30), 1));
    }

    public async Task<StartupMigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await using var migrationLock = await DatabaseMigrationLock.AcquireAsync(
            context.Database.GetConnectionString()!, _lockTimeout, _logger, cancellationToken);

        // Se consulta con el bloqueo tomado: si otro proceso migró mientras se esperaba, ya no queda nada pendiente.
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
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
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        if (applied.Count > 0)
        {
            var backup = await _backupService.CreatePreMigrationBackupAsync(
                $"{applied.Last()} → {pending.Last()}", cancellationToken);
            if (!backup.Success)
            {
                throw new InvalidOperationException($"No se aplicaron las migraciones porque falló el respaldo previo: {backup.Message}");
            }
            _logger.LogInformation("Respaldo previo a las migraciones: {Path}", backup.BackupFilePath);
        }

        await context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Todas las migraciones se aplicaron correctamente");
        return new StartupMigrationResult(StartupMigrationOutcome.Migrated, $"Se aplicaron {pending.Count} migraciones.");
    }
}
