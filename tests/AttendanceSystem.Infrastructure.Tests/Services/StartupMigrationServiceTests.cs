using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

/// <summary>
/// Migraciones al arrancar contra PostgreSQL real, cada prueba sobre una base nueva sin migrar.
/// </summary>
public class StartupMigrationServiceTests : IAsyncLifetime
{
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
    }

    private StartupMigrationService NewService(Dictionary<string, string?>? settings = null) => new(
        new ContextFactory(_connectionString),
        NullLogger<StartupMigrationService>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build());

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
