using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Paso de deduplicación de checadas de <c>AddAttendanceIntegrityAndOutbox</c> contra PostgreSQL real, sobre una base
/// en la última migración anterior (como las instalaciones que se actualizan).
/// </summary>
public class PunchDeduplicationMigrationTests : IAsyncLifetime
{
    private const string Before = "20261009153817_AddEmployeeAuditLogs";
    private const string Dedup = "20261017160000_AddAttendanceIntegrityAndOutbox";

    private readonly string _database = $"attendance_dedup_{Guid.NewGuid():N}";
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
    public async Task KeepsOnePunchPerGroup_PreferringTheLinkedOne_AndRepointsTheDays()
    {
        if (!_available) return;
        await MigrateToAsync(Before);

        // Grupo 1: la copia que usa una jornada gana aunque otra tenga mayor estado.
        await InsertPunchAsync("10000000-0000-0000-0000-000000000001", "E1", "2026-10-01 08:00", status: 0);
        await InsertPunchAsync("10000000-0000-0000-0000-000000000002", "E1", "2026-10-01 08:00", status: 1);
        await InsertPunchAsync("10000000-0000-0000-0000-000000000003", "E1", "2026-10-01 08:00", status: 0);
        await InsertDayAsync("E1", "2026-10-01", checkOut: "10000000-0000-0000-0000-000000000003");
        // Grupo 2: sin jornada, gana el de mayor estado.
        await InsertPunchAsync("20000000-0000-0000-0000-000000000001", "E1", "2026-10-02 08:00", status: 0);
        await InsertPunchAsync("20000000-0000-0000-0000-000000000002", "E1", "2026-10-02 08:00", status: 2);
        // Grupo 3: dos copias enlazadas por jornadas distintas; gana la de menor Id y ambas jornadas apuntan a ella.
        await InsertPunchAsync("30000000-0000-0000-0000-000000000001", "E2", "2026-10-03 08:00", status: 0);
        await InsertPunchAsync("30000000-0000-0000-0000-000000000002", "E2", "2026-10-03 08:00", status: 0);
        await InsertDayAsync("E2", "2026-10-03", checkIn: "30000000-0000-0000-0000-000000000002");
        await InsertDayAsync("E2", "2026-10-04", checkIn: "30000000-0000-0000-0000-000000000001");
        // Sin repetir: no se toca.
        await InsertPunchAsync("40000000-0000-0000-0000-000000000001", "E2", "2026-10-05 08:00", status: 0);

        await MigrateToAsync(Dedup);

        (await QueryAsync(@"SELECT ""Id""::text FROM ""AttendanceRecords"" ORDER BY 1")).Should().Equal(
            "10000000-0000-0000-0000-000000000003",
            "20000000-0000-0000-0000-000000000002",
            "30000000-0000-0000-0000-000000000001",
            "40000000-0000-0000-0000-000000000001");
        (await QueryAsync(@"SELECT coalesce(""CheckInRecordId"", ""CheckOutRecordId"")::text FROM ""DailyAttendances"" ORDER BY ""Date""")).Should().Equal(
            "10000000-0000-0000-0000-000000000003",
            "30000000-0000-0000-0000-000000000001",
            "30000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public async Task OnARealisticVolume_FinishesInSeconds()
    {
        if (!_available) return;
        await MigrateToAsync(Before);

        // 200 000 checadas (una de cada 60 repetida) y 50 000 jornadas que usan algunas. La versión anterior buscaba
        // cada checada en todas las jornadas: 10^10 comparaciones.
        await ExecuteAsync(_connectionString, @"
            INSERT INTO ""AttendanceRecords"" (""Id"", ""EmployeeId"", ""CheckTime"", ""CheckTypeId"", ""DeviceId"", ""StatusId"", ""VerifyMethodId"")
            SELECT gen_random_uuid(), 'E' || (i % 500), timestamptz '2025-01-01' + make_interval(mins => i), 0, 'D1', 0, 0
              FROM generate_series(1, 200000) i;
            INSERT INTO ""AttendanceRecords"" (""Id"", ""EmployeeId"", ""CheckTime"", ""CheckTypeId"", ""DeviceId"", ""StatusId"", ""VerifyMethodId"")
            SELECT gen_random_uuid(), ""EmployeeId"", ""CheckTime"", 0, 'D2', 1, 0
              FROM ""AttendanceRecords"" WHERE extract(minute FROM ""CheckTime"") = 7;
            ANALYZE ""AttendanceRecords"";");
        await InsertDaysAsync(@"FROM (SELECT ""Id"" AS punch, row_number() OVER ()::int AS i FROM ""AttendanceRecords"") r WHERE i % 4 = 0 AND i <= 200000",
            employee: "'E' || (i % 500)", date: "date '2000-01-01' + i", checkIn: "punch");

        var elapsed = Stopwatch.StartNew();
        await MigrateToAsync(Dedup);

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1));
        (await QueryAsync(@"SELECT count(*)::text FROM (SELECT 1 FROM ""AttendanceRecords"" GROUP BY ""EmployeeId"", ""CheckTime"" HAVING count(*) > 1) g"))
            .Should().Equal("0");
    }

    private Task InsertPunchAsync(string id, string employee, string checkTime, int status) => ExecuteAsync(_connectionString, $@"
        INSERT INTO ""AttendanceRecords"" (""Id"", ""EmployeeId"", ""CheckTime"", ""CheckTypeId"", ""DeviceId"", ""StatusId"", ""VerifyMethodId"")
        VALUES ('{id}', '{employee}', timestamptz '{checkTime}+00', 0, 'D1', {status}, 0);");

    private Task InsertDayAsync(string employee, string date, string? checkIn = null, string? checkOut = null) =>
        InsertDaysAsync("", $"'{employee}'", $"date '{date}'",
            checkIn != null ? $"uuid '{checkIn}'" : "NULL", checkOut != null ? $"uuid '{checkOut}'" : "NULL");

    /// <summary>
    /// Jornadas con el esquema anterior: cada columna obligatoria sin valor por defecto recibe un cero de su tipo.
    /// </summary>
    private async Task InsertDaysAsync(string from, string employee, string date, string checkIn = "NULL", string checkOut = "NULL")
    {
        var values = new Dictionary<string, string>
        {
            ["Id"] = "gen_random_uuid()",
            ["EmployeeId"] = employee,
            ["Date"] = date,
            ["CheckInRecordId"] = checkIn,
            ["CheckOutRecordId"] = checkOut
        };
        var required = await QueryAsync(@"
            SELECT column_name || '|' || CASE
                WHEN data_type IN ('integer','bigint','smallint','numeric','double precision','real') THEN '0'
                WHEN data_type = 'boolean' THEN 'false'
                WHEN data_type = 'uuid' THEN 'gen_random_uuid()'
                WHEN data_type = 'date' OR data_type LIKE 'timestamp%' THEN 'now()'
                WHEN data_type = 'interval' THEN 'interval ''0'''
                WHEN data_type LIKE 'time%' THEN 'time ''00:00'''
                ELSE '''''' END
              FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = 'DailyAttendances'
               AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'");
        foreach (var column in required.Select(r => r.Split('|', 2)))
        {
            values.TryAdd(column[0], column[1]);
        }

        await ExecuteAsync(_connectionString, $@"
            INSERT INTO ""DailyAttendances"" ({string.Join(", ", values.Keys.Select(k => $"\"{k}\""))})
            SELECT {string.Join(", ", values.Values)} {from};");
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private async Task MigrateToAsync(string migration)
    {
        await using var context = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseNpgsql(_connectionString).Options,
            new Moq.Mock<MediatR.IPublisher>().Object);
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 };
        await command.ExecuteNonQueryAsync();
    }
}
