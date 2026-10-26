using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Services;
using AttendanceSystem.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

/// <summary>
/// Respaldo y restauración contra PostgreSQL real con pg_dump, pg_restore y psql. La restauración debe ser atómica:
/// si falla en cualquier punto antes del COMMIT, la base queda exactamente como estaba.
/// Las herramientas se toman de TEST_PG_BIN_PATH o de la instalación local; en CI (REQUIRE_POSTGRES_TESTS) son obligatorias.
/// </summary>
public class BackupRestorePostgresTests : IClassFixture<PostgresTestDatabaseFixture>, IDisposable
{
    private readonly PostgresTestDatabaseFixture _fixture;
    private readonly string _backupDir;
    private readonly Mock<ISystemConfigurationRepository> _configRepo = new();

    public BackupRestorePostgresTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _backupDir = Path.Combine(Path.GetTempPath(), $"backup_restore_pg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_backupDir);

        var config = SystemConfiguration.CreateDefault();
        config.UpdateBackupDirectory(_backupDir);
        _configRepo.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(config);
    }

    public void Dispose()
    {
        try { Directory.Delete(_backupDir, true); } catch { }
    }

    private IConfiguration BuildConfiguration(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AttendanceDb"] = _fixture.ConnectionString,
            ["Backup:KeepLastNBackups"] = "0",
            ["Backup:DrainTimeoutSeconds"] = "1",
            // Las pruebas refrescan el estado de otros procesos a mano.
            ["Maintenance:PollIntervalSeconds"] = "0"
        };
        var binPath = Environment.GetEnvironmentVariable("TEST_PG_BIN_PATH");
        if (!string.IsNullOrWhiteSpace(binPath))
        {
            values["Backup:PostgresBinPath"] = binPath;
        }
        foreach (var kv in extra ?? new())
        {
            values[kv.Key] = kv.Value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private (BackupService Service, RestoreStateService State) CreateService()
    {
        var configuration = BuildConfiguration();
        var state = NewState();
        var service = new BackupService(configuration, NullLogger<BackupService>.Instance, _configRepo.Object, state,
            contextFactory: new FixtureContextFactory(_fixture));
        return (service, state);
    }

    /// <summary>Cada instancia representa otro proceso (servidor web, cliente WPF, otro worker de IIS).</summary>
    private RestoreStateService NewState() => new(NullLogger<RestoreStateService>.Instance, BuildConfiguration());

    private sealed class FixtureContextFactory : IDbContextFactory<AttendanceDbContext>
    {
        private readonly PostgresTestDatabaseFixture _fixture;
        public FixtureContextFactory(PostgresTestDatabaseFixture fixture) => _fixture = fixture;
        public AttendanceDbContext CreateDbContext() => _fixture.CreateDbContext();
    }

    /// <summary>Hay PostgreSQL y herramientas cliente; si faltan en CI, la prueba falla en vez de omitirse en silencio.</summary>
    private bool ToolsAvailable(BackupService service)
    {
        var available = _fixture.IsAvailable
                        && !string.IsNullOrEmpty(service.FindPgToolPath("pg_dump"))
                        && !string.IsNullOrEmpty(service.FindPgToolPath("pg_restore"))
                        && !string.IsNullOrEmpty(service.FindPgToolPath("psql"));

        if (!available && string.Equals(Environment.GetEnvironmentVariable("REQUIRE_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("REQUIRE_POSTGRES_TESTS=true pero faltan PostgreSQL o pg_dump/pg_restore/psql.");
        }
        return available;
    }

    private async Task ExecAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private Task<object?> ProbeValueAsync() => ScalarAsync("SELECT value FROM public.restore_probe WHERE id = 1");

    private async Task SetProbeAsync(string value)
    {
        await ExecAsync($@"
            CREATE TABLE IF NOT EXISTS public.restore_probe (id int PRIMARY KEY, value text NOT NULL);
            INSERT INTO public.restore_probe (id, value) VALUES (1, '{value}')
            ON CONFLICT (id) DO UPDATE SET value = EXCLUDED.value;");
    }

    private static HashSet<string> RestoreTempDirs() =>
        Directory.GetDirectories(Path.GetTempPath(), "AttendanceRestore_*").ToHashSet();

    private async Task AssertSchemaIntactAsync()
    {
        var tables = Convert.ToInt32(await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('Employees', 'AttendanceRecords')"));
        tables.Should().Be(2, "una restauración fallida no debe dejar la base sin esquema");
    }

    [Fact]
    public async Task FullBackup_ThenRestore_BringsBackTheBackedUpData_AndCleansUp()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        await SetProbeAsync("antes");
        var backup = await service.CreateFullBackupAsync("prueba");
        backup.Success.Should().BeTrue(backup.Message);
        await SetProbeAsync("despues");

        var tempDirsBefore = RestoreTempDirs();
        var result = await service.RestoreBackupAsync(backup.BackupFilePath!);

        result.Success.Should().BeTrue(result.Message);
        (await ProbeValueAsync()).Should().Be("antes");
        state.CurrentState.Should().Be(MaintenanceState.Normal);
        RestoreTempDirs().Except(tempDirsBefore).Should().BeEmpty("el respaldo extraído no debe quedar en el directorio temporal");
        Directory.GetFiles(_backupDir, ".safety_snapshot_*").Should().BeEmpty();
        (await ScalarAsync("SELECT state FROM system_maintenance.maintenance_lock WHERE id = 1")).Should().Be("Normal");
    }

    [Fact]
    public async Task Restore_WhenTheScriptFailsHalfway_LeavesTheDatabaseUnchanged()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        // Un respaldo con una tabla cuyo dueño ya no existe: el ALTER ... OWNER TO falla a mitad del script,
        // después de que el preámbulo borró los esquemas.
        await ExecAsync(@"
            DROP ROLE IF EXISTS restore_probe_owner;
            CREATE ROLE restore_probe_owner;
            CREATE TABLE public.owned_probe (id int);
            ALTER TABLE public.owned_probe OWNER TO restore_probe_owner;");
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);
        await ExecAsync("DROP TABLE public.owned_probe; DROP ROLE restore_probe_owner;");
        await SetProbeAsync("vigente");

        var result = await service.RestoreBackupAsync(backup.BackupFilePath!);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("restore_probe_owner", "debe fallar dentro del script, después del borrado de esquemas");
        result.Message.Should().Contain("no se modificó");
        (await ProbeValueAsync()).Should().Be("vigente");
        await AssertSchemaIntactAsync();
        state.CurrentState.Should().Be(MaintenanceState.Normal);
        Directory.GetFiles(_backupDir, ".safety_snapshot_*").Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_OfATruncatedArchive_NeverCommitsAPartialScript()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        // Suficientes datos para que el corte caiga en los bloques de datos y no en el índice (TOC) del archivo.
        await ExecAsync(@"
            DROP TABLE IF EXISTS public.bulk_probe;
            CREATE TABLE public.bulk_probe AS SELECT g AS id, md5(g::text) AS payload FROM generate_series(1, 200000) g;");
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);

        var truncated = Path.Combine(_backupDir, "truncated.backup");
        var bytes = await File.ReadAllBytesAsync(backup.BackupFilePath!);
        await File.WriteAllBytesAsync(truncated, bytes.Take(bytes.Length * 7 / 10).ToArray());

        await ExecAsync("DROP TABLE public.bulk_probe;");
        await SetProbeAsync("vigente");

        var result = await service.RestoreBackupAsync(truncated);

        result.Success.Should().BeFalse();
        (await ProbeValueAsync()).Should().Be("vigente");
        (await ScalarAsync("SELECT to_regclass('public.bulk_probe')::text")).Should().BeNull("los datos del respaldo truncado no deben confirmarse");
        await AssertSchemaIntactAsync();
        state.CurrentState.Should().Be(MaintenanceState.Normal);
    }

    [Fact]
    public async Task Restore_WhenAWriterDoesNotFinish_AbortsBeforeTouchingTheDatabase()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        await SetProbeAsync("antes");
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);
        await SetProbeAsync("vigente");

        AttendanceSystem.Application.DTOs.RestoreResultDto result;
        using (state.RegisterActiveWriter())
        {
            result = await service.RestoreBackupAsync(backup.BackupFilePath!);
        }

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("siguen en curso");
        (await ProbeValueAsync()).Should().Be("vigente");
        state.CurrentState.Should().Be(MaintenanceState.Normal);
        Directory.GetFiles(_backupDir, ".safety_snapshot_*").Should().BeEmpty("el snapshot se toma después de drenar");
    }

    [Fact]
    public async Task Backups_DoNotCarryTheMaintenanceSchema()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        await state.TransitionStateAsync(MaintenanceState.Normal, "asegurar tabla de mantenimiento");
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);

        var psi = new System.Diagnostics.ProcessStartInfo(service.FindPgToolPath("pg_restore"), $"-l \"{backup.BackupFilePath}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var toc = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        toc.Should().Contain("restore_probe").And.NotContain("system_maintenance");
    }

    [Fact]
    public async Task PersistedFailedState_SurvivesARestart_AndASuccessfulRestoreClearsIt()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        await SetProbeAsync("antes");
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);

        (await state.TryAcquireRestoreLockAsync("previo", "simulación")).Should().BeTrue();
        await state.ReleaseRestoreLockAsync("previo", success: false);

        // "Reinicio": un servicio nuevo sólo conoce lo persistido.
        var configuration = BuildConfiguration();
        var restarted = new RestoreStateService(NullLogger<RestoreStateService>.Instance, configuration);
        (await restarted.LoadPersistedStateAsync()).Should().Be(MaintenanceState.Failed);
        restarted.CanWrite.Should().BeFalse();

        var restartedService = new BackupService(configuration, NullLogger<BackupService>.Instance, _configRepo.Object, restarted);
        var result = await restartedService.RestoreBackupAsync(backup.BackupFilePath!);

        result.Success.Should().BeTrue(result.Message);
        restarted.CurrentState.Should().Be(MaintenanceState.Normal);
        var afterSecondRestart = new RestoreStateService(NullLogger<RestoreStateService>.Instance, configuration);
        (await afterSecondRestart.LoadPersistedStateAsync()).Should().Be(MaintenanceState.Normal);
    }

    [Fact]
    public async Task PersistedRestoringState_IsDiscardedOnRestart_BecauseNothingWasCommitted()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        (await state.TryAcquireRestoreLockAsync("interrumpida", "simulación")).Should().BeTrue();
        await state.TransitionStateAsync(MaintenanceState.Restoring, "simulación");
        state.Dispose(); // el proceso muere: su sesión y el bloqueo consultivo desaparecen

        var restarted = new RestoreStateService(NullLogger<RestoreStateService>.Instance, BuildConfiguration());
        (await restarted.LoadPersistedStateAsync()).Should().Be(MaintenanceState.Normal);
        (await ScalarAsync("SELECT state FROM system_maintenance.maintenance_lock WHERE id = 1")).Should().Be("Normal");
    }

    [Fact]
    public async Task AnotherProcess_SeesARestoreInProgress_AndCannotStartItsOwn()
    {
        var (service, restorer) = CreateService();
        if (!ToolsAvailable(service)) return;
        using var other = NewState();

        (await restorer.TryAcquireRestoreLockAsync("web", "restauración")).Should().BeTrue();
        await other.RefreshRemoteStateAsync();

        other.CanWrite.Should().BeFalse();
        other.CurrentState.Should().Be(MaintenanceState.Preparing);
        other.CurrentOwner.Should().Be("web");
        (await other.TryAcquireRestoreLockAsync("wpf", "restauración simultánea")).Should().BeFalse();

        // La restauración termina las demás conexiones, pero no la que prueba que sigue viva.
        await service.TerminateOtherConnectionsAsync(CancellationToken.None);
        await other.RefreshRemoteStateAsync();
        other.CurrentState.Should().Be(MaintenanceState.Preparing);

        await restorer.ReleaseRestoreLockAsync("web", success: true);
        await other.RefreshRemoteStateAsync();
        other.CanWrite.Should().BeTrue();
        (await other.TryAcquireRestoreLockAsync("wpf", "ahora sí")).Should().BeTrue();
        await other.ReleaseRestoreLockAsync("wpf", success: true);
    }

    [Fact]
    public async Task ARestoreLeftBehindByADeadProcess_IsIgnoredBeforeCommit_AndFailedAfterIt()
    {
        var (service, _) = CreateService();
        if (!ToolsAvailable(service)) return;
        using var other = NewState();

        var crashedBeforeCommit = NewState();
        (await crashedBeforeCommit.TryAcquireRestoreLockAsync("muerto-1", "simulación")).Should().BeTrue();
        await crashedBeforeCommit.TransitionStateAsync(MaintenanceState.Restoring, "simulación");
        crashedBeforeCommit.Dispose();

        await other.RefreshRemoteStateAsync();
        other.CanWrite.Should().BeTrue("antes del COMMIT la base no cambió");

        var crashedAfterCommit = NewState();
        (await crashedAfterCommit.TryAcquireRestoreLockAsync("muerto-2", "simulación")).Should().BeTrue();
        await crashedAfterCommit.TransitionStateAsync(MaintenanceState.Validating, "simulación");
        crashedAfterCommit.Dispose();

        await other.RefreshRemoteStateAsync();
        other.CurrentState.Should().Be(MaintenanceState.Failed, "los datos restaurados no llegaron a validarse");
        other.CanWrite.Should().BeFalse();

        (await other.TryAcquireRestoreLockAsync("recuperación", "desde Failed")).Should().BeTrue();
        await other.ReleaseRestoreLockAsync("recuperación", success: true);
        other.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task AFailedRestoreInAnotherProcess_BlocksWritesHereToo()
    {
        var (service, _) = CreateService();
        if (!ToolsAvailable(service)) return;
        using var failing = NewState();
        using var other = NewState();

        (await failing.TryAcquireRestoreLockAsync("wpf", "simulación")).Should().BeTrue();
        await failing.ReleaseRestoreLockAsync("wpf", success: false);
        await other.RefreshRemoteStateAsync();

        other.CurrentState.Should().Be(MaintenanceState.Failed);
        other.Invoking(o => o.RegisterActiveWriter()).Should().Throw<AttendanceSystem.Application.Exceptions.DatabaseMaintenanceException>();

        (await failing.TryAcquireRestoreLockAsync("wpf", "limpieza")).Should().BeTrue();
        await failing.ReleaseRestoreLockAsync("wpf", success: true);
        await other.RefreshRemoteStateAsync();
        other.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_OfABackupFromANewerVersion_IsRejectedAndRolledBack()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        await ExecAsync(@"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('29991231000000_FutureMigration', '99.0.0')");
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);
        await ExecAsync(@"DELETE FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '29991231000000_FutureMigration'");
        await SetProbeAsync("vigente");

        var result = await service.RestoreBackupAsync(backup.BackupFilePath!);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("versión más nueva").And.Contain("revertida exitosamente");
        (await ProbeValueAsync()).Should().Be("vigente");
        (await ScalarAsync(@"SELECT COUNT(*) FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '29991231000000_FutureMigration'"))
            .Should().Be(0L);
        state.CurrentState.Should().Be(MaintenanceState.Normal);
    }

    [Fact]
    public async Task Restore_OfABackupFromAnOlderVersion_AppliesThePendingMigrations()
    {
        var (service, state) = CreateService();
        if (!ToolsAvailable(service)) return;

        string latest;
        await using (var context = _fixture.CreateDbContext())
        {
            var migrations = context.Database.GetMigrations().ToList();
            latest = migrations[^1];
            await context.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        }
        var backup = await service.CreateDatabaseBackupAsync();
        backup.Success.Should().BeTrue(backup.Message);
        await using (var context = _fixture.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var result = await service.RestoreBackupAsync(backup.BackupFilePath!);

        result.Success.Should().BeTrue(result.Message);
        state.CurrentState.Should().Be(MaintenanceState.Normal);
        await using (var context = _fixture.CreateDbContext())
        {
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            (await context.Database.GetAppliedMigrationsAsync()).Should().Contain(latest);
        }
    }
}
