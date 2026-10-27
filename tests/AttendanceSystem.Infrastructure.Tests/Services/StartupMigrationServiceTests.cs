using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

/// <summary>
/// Migraciones al arrancar contra PostgreSQL real, cada prueba sobre una base nueva sin migrar.
/// </summary>
public class StartupMigrationServiceTests : IAsyncLifetime
{
    /// <summary>Última migración de v2.2.3: desde ahí la actualización incluye migraciones que eliminan datos.</summary>
    private const string V223LastMigration = "20261003231726_AddExternalEmployees";

    private readonly string _backupDir = Path.Combine(Path.GetTempPath(), $"startup_migration_{Guid.NewGuid():N}");
    private readonly Mock<IBackupService> _backupService = new();
    private readonly string _database = $"attendance_startup_{Guid.NewGuid():N}";
    private string _admin = string.Empty;
    private string _connectionString = string.Empty;
    private bool _available;

    public async Task InitializeAsync()
    {
        var env = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        _admin = new NpgsqlConnectionStringBuilder(string.IsNullOrWhiteSpace(env)
            ? "Host=localhost;Port=5432;Username=attendanceuser;Password=AttendancePass;"
            : env) { Database = "postgres" }.ConnectionString;
        _connectionString = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;

        try
        {
            await ExecuteAsync(_admin, $"CREATE DATABASE \"{_database}\";");
            _available = true;
        }
        catch (Exception) when (!string.Equals(Environment.GetEnvironmentVariable("REQUIRE_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        try { Directory.Delete(_backupDir, true); } catch { }
        if (!_available) return;
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(_admin, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);");
    }

    [Fact]
    public async Task ConcurrentStarts_AllSucceed_AndApplyEachMigrationOnce()
    {
        if (!_available) return;

        // Antes, dos de tres arranques simultáneos fallaban con "relation ... already exists".
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => NewService().RunAsync())));

        results.Count(r => r.Outcome == StartupMigrationOutcome.Migrated).Should().Be(1, "sólo el primero en tomar el bloqueo migra");
        results.Count(r => r.Outcome == StartupMigrationOutcome.UpToDate).Should().Be(2, "los demás esperan y ya no encuentran migraciones pendientes");

        await using var context = new ContextFactory(_connectionString).CreateDbContext();
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        _backupService.Verify(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never(),
            "una base recién creada no tiene datos que respaldar");
    }

    [Fact]
    public async Task UpgradeFromOlderVersion_BacksUpTheOldSchemaFirst_IntoTheConfiguredDirectory()
    {
        if (!_available) return;
        await MigrateToAsync(V223LastMigration);
        await SeedOldSystemConfigurationAsync(_backupDir);

        var backupService = RealBackupService();
        if (backupService == null) return;

        var result = await NewService(backupService).RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Migrated);
        // El directorio sale de la configuración guardada aunque el modelo actual no pueda leer esa fila todavía.
        var backups = (await backupService.GetAvailableBackupsAsync()).ToList();
        backups.Should().ContainSingle(b => b.FileName.StartsWith("PreMigration_Backup_"))
            .Which.Description.Should().Be("Estado previo a una actualización del esquema");

        await backupService.PruneOldBackupsAsync(keepCount: 1);
        Directory.GetFiles(_backupDir, "PreMigration_Backup_*").Should().HaveCount(1, "la retención no borra el respaldo previo a migrar");
    }

    [Fact]
    public async Task WhenThePreMigrationBackupFails_NothingIsMigrated_AndTheAppStartsInMaintenance()
    {
        if (!_available) return;
        await MigrateToAsync(V223LastMigration);
        _backupService.Setup(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = false, Message = "pg_dump no encontrado" });
        var state = NewState();

        var result = await NewService(state: state).RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Maintenance);
        result.Message.Should().Contain("respaldo previo").And.Contain("pg_dump no encontrado");
        state.CurrentState.Should().Be(MaintenanceState.Failed);
        (await LastAppliedMigrationAsync()).Should().Be(V223LastMigration, "sin respaldo no se toca la base");

        // Otro proceso (un cliente WPF) ve el mantenimiento y deja de escribir.
        var other = NewState();
        await other.RefreshRemoteStateAsync();
        other.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task AfterAStartupMaintenance_TheNextStartRetries()
    {
        if (!_available) return;
        await MigrateToAsync(V223LastMigration);
        _backupService.SetupSequence(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = false, Message = "disco lleno" })
            .ReturnsAsync(new BackupResultDto { Success = true, BackupFilePath = "/respaldos/PreMigration_Backup_x.backup" });
        (await NewService(state: NewState()).RunAsync()).Outcome.Should().Be(StartupMigrationOutcome.Maintenance);

        // Reinicio tras liberar espacio: el mantenimiento lo dejó un arranque, no una restauración, así que se reintenta.
        var restarted = NewState();
        (await restarted.LoadPersistedStateAsync()).Should().Be(MaintenanceState.Normal);
        (await NewService(state: restarted).RunAsync()).Outcome.Should().Be(StartupMigrationOutcome.Migrated);
        restarted.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task WhenAMigrationFails_TheDatabaseStaysAtTheLastCompletedOne_AndTheAppStartsInMaintenance()
    {
        if (!_available) return;
        await MigrateToAsync(V223LastMigration);
        // La penúltima migración pendiente ya no puede crear su tabla.
        await ExecuteAsync(_connectionString, @"CREATE TABLE ""DeviceClockIncidents"" (""Id"" uuid PRIMARY KEY);");
        _backupService.Setup(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = true, BackupFilePath = "/respaldos/PreMigration_Backup_x.backup" });
        var state = NewState();

        var result = await NewService(state: state).RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Maintenance);
        var lastApplied = await LastAppliedMigrationAsync();
        lastApplied.Should().Be("20261107120000_AttendancePeriodClosures", "cada migración se confirma por separado");
        result.Message.Should().Contain(lastApplied).And.Contain("/respaldos/PreMigration_Backup_x.backup");
        state.CurrentState.Should().Be(MaintenanceState.Failed);
    }

    [Fact]
    public async Task WhenTheDatabaseIsFromANewerVersion_NothingIsTouched_AndTheAppStartsInMaintenance()
    {
        if (!_available) return;
        await MigrateToAsync("20261109120000_AddEmployeeStatusBranchDepartmentIndex");
        await ExecuteAsync(_connectionString,
            @"INSERT INTO ""__EFMigrationsHistory"" VALUES ('29991231000000_FromANewerVersion', '10.0.10');");
        var state = NewState();

        var result = await NewService(state: state).RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Maintenance);
        result.Message.Should().Contain("versión más nueva").And.Contain("29991231000000_FromANewerVersion");
        state.CurrentState.Should().Be(MaintenanceState.Failed);
        _backupService.Verify(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task AnInstallWithTheRetiredOvertimeFlagsMigrationId_IsUpgraded_NotTreatedAsNewer()
    {
        if (!_available) return;
        // Esas instalaciones aplicaron AddOvertimeFlagsToDailyAttendance con el identificador que tenía antes de regenerarse.
        await MigrateToAsync(V223LastMigration);
        await ExecuteAsync(_connectionString, @"
DELETE FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '20260423210254_AddOvertimeFlagsToDailyAttendance';
INSERT INTO ""__EFMigrationsHistory"" VALUES ('20260423195140_AddOvertimeFlagsToDailyAttendance', '9.0.0');");
        _backupService.Setup(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = true, BackupFilePath = "/respaldos/PreMigration_Backup_x.backup" });

        var result = await NewService().RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Migrated);
        await using var context = new ContextFactory(_connectionString).CreateDbContext();
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty("la migración regenerada no choca con las columnas que ya existen");
    }

    [Fact]
    public async Task WhenAClientHoldsATable_TheMigrationGivesUpQuickly_AndTheMessageNamesTheClient()
    {
        if (!_available) return;
        await MigrateToAsync(V223LastMigration);
        _backupService.Setup(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = true, BackupFilePath = "/respaldos/PreMigration_Backup_x.backup" });
        // Un cliente WPF que dejó una transacción abierta después de leer checadas.
        await using var client = await OpenClientAsync("cliente_wpf_prueba",
            @"BEGIN; SELECT 1 FROM ""AttendanceRecords"" LIMIT 1;");
        var state = NewState();

        var result = await NewService(state: state, lockTimeoutSeconds: 2).RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Maintenance);
        result.Message.Should().Contain("soltaran tablas").And.Contain("cliente_wpf_prueba").And.Contain("AttendanceRecords");
        state.CurrentState.Should().Be(MaintenanceState.Failed);
        await using var context = new ContextFactory(_connectionString).CreateDbContext();
        (await context.Database.GetPendingMigrationsAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task WhenAnOrphanedSessionHoldsTheHistory_StartupDoesNotHang_AndNamesTheSession()
    {
        if (!_available) return;
        await MigrateToAsync(V223LastMigration);
        // La sesión de un proceso que murió a mitad de una migración: ya no tiene el bloqueo consultivo, pero sí el historial.
        await using var orphan = await OpenClientAsync("migracion_huerfana",
            @"BEGIN; LOCK TABLE ""__EFMigrationsHistory"" IN ACCESS EXCLUSIVE MODE;");

        var result = await NewService(lockTimeoutSeconds: 2).RunAsync();

        result.Outcome.Should().Be(StartupMigrationOutcome.Maintenance);
        result.Message.Should().Contain("soltaran tablas").And.Contain("migracion_huerfana").And.Contain("__EFMigrationsHistory");
        _backupService.Verify(b => b.CreatePreMigrationBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task WhenPostgresIsUnreachable_StartupRetriesAndThenStops()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(_connectionString) { Port = 1, Timeout = 2 }.ConnectionString;
        var logger = new Mock<ILogger<StartupMigrationService>>();
        var service = new StartupMigrationService(new ContextFactory(unreachable), _backupService.Object,
            new RestoreStateService(NullLogger<RestoreStateService>.Instance), logger.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:StartupConnectAttempts"] = "3",
                ["Database:StartupConnectRetrySeconds"] = "0"
            }).Build());

        // Sin base no hay pantalla de mantenimiento que mostrar: el arranque falla y el siguiente lo reintenta.
        await service.Invoking(s => s.RunAsync()).Should().ThrowAsync<Exception>();
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("No se pudo conectar con PostgreSQL")),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(2));
    }

    private StartupMigrationService NewService(IBackupService? backupService = null, RestoreStateService? state = null,
        int? lockTimeoutSeconds = null) => new(
        new ContextFactory(_connectionString),
        backupService ?? _backupService.Object,
        state ?? NewState(),
        NullLogger<StartupMigrationService>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigrationLockTimeoutSeconds"] = lockTimeoutSeconds?.ToString()
        }).Build());

    /// <summary>Otra sesión que ejecuta <paramref name="sql"/> y queda abierta hasta que se libere.</summary>
    private async Task<NpgsqlConnection> OpenClientAsync(string applicationName, string sql)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_connectionString)
        {
            ApplicationName = applicationName,
            Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    /// <summary>Cada instancia representa otro proceso o un reinicio; el estado de los demás se refresca a mano.</summary>
    private RestoreStateService NewState() => new(NullLogger<RestoreStateService>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AttendanceDb"] = _connectionString,
            ["Maintenance:PollIntervalSeconds"] = "0"
        }).Build());

    private async Task<string?> LastAppliedMigrationAsync()
    {
        await using var context = new ContextFactory(_connectionString).CreateDbContext();
        return (await context.Database.GetAppliedMigrationsAsync()).LastOrDefault();
    }

    /// <summary>Respaldo real con pg_dump; null si faltan las herramientas fuera de CI.</summary>
    private BackupService? RealBackupService()
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:AttendanceDb"] = _connectionString };
        var binPath = Environment.GetEnvironmentVariable("TEST_PG_BIN_PATH");
        if (!string.IsNullOrWhiteSpace(binPath)) values["Backup:PostgresBinPath"] = binPath;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var factory = new ContextFactory(_connectionString);
        var service = new BackupService(configuration, NullLogger<BackupService>.Instance,
            new SystemConfigurationRepository(factory.CreateDbContext()), contextFactory: factory);

        if (string.IsNullOrEmpty(service.FindPgToolPath("pg_dump")) || string.IsNullOrEmpty(service.FindPgToolPath("pg_restore")))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("REQUIRE_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("REQUIRE_POSTGRES_TESTS=true pero faltan pg_dump/pg_restore.");
            }
            return null;
        }
        return service;
    }

    private async Task MigrateToAsync(string migration)
    {
        await using var context = new ContextFactory(_connectionString).CreateDbContext();
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    /// <summary>Fila de configuración con el esquema viejo: cada columna obligatoria sin valor por defecto recibe un cero de su tipo.</summary>
    private async Task SeedOldSystemConfigurationAsync(string backupDirectory)
    {
        await ExecuteAsync(_connectionString, @"
DO $$
DECLARE cols text; vals text;
BEGIN
    SELECT string_agg(quote_ident(column_name), ','),
           string_agg(CASE
               WHEN data_type IN ('integer','bigint','smallint','numeric','double precision','real') THEN '0'
               WHEN data_type = 'boolean' THEN 'false'
               WHEN data_type = 'uuid' THEN 'gen_random_uuid()'
               WHEN data_type IN ('date') OR data_type LIKE 'timestamp%' THEN 'now()'
               WHEN data_type = 'interval' THEN '''0'''
               WHEN data_type LIKE 'time%' THEN '''00:00'''
               ELSE '''''' END, ',')
      INTO cols, vals
      FROM information_schema.columns
     WHERE table_schema = 'public' AND table_name = 'SystemConfiguration'
       AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO';
    EXECUTE format('INSERT INTO ""SystemConfiguration"" (%s) VALUES (%s)', cols, vals);
END $$;");
        await ExecuteAsync(_connectionString, $@"UPDATE ""SystemConfiguration"" SET ""BackupDirectory"" = '{backupDirectory}';");
        Directory.CreateDirectory(backupDirectory);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ContextFactory : IDbContextFactory<AttendanceDbContext>
    {
        private readonly string _connectionString;
        public ContextFactory(string connectionString) => _connectionString = connectionString;

        public AttendanceDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseNpgsql(_connectionString).Options,
            new Moq.Mock<MediatR.IPublisher>().Object);
    }
}
