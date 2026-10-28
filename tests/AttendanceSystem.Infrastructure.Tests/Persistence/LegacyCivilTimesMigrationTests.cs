using System;
using System.Collections.Generic;
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
/// <c>ConvertLegacyCivilTimes</c> contra PostgreSQL real, en una base cuya zona es America/Chicago: así quedan a la
/// vista los cálculos que dependen de la zona de la sesión.
/// </summary>
public class LegacyCivilTimesMigrationTests : IAsyncLifetime
{
    private const string V223LastMigration = "20261003231726_AddExternalEmployees";
    private const string Before = "20261109120000_AddEmployeeStatusBranchDepartmentIndex";
    private const string Convert = "20261110120000_ConvertLegacyCivilTimes";

    private readonly string _database = $"attendance_civil_{Guid.NewGuid():N}";
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
            await ExecuteAsync(_admin, $"ALTER DATABASE \"{_database}\" SET timezone TO 'America/Chicago';");
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
    public async Task LegacyPunches_GetTheirRealInstant_AndKeepTheClockTime()
    {
        if (!_available) return;
        await MigrateToAsync(Before);
        await InsertAsync("Devices", new() { ["Id"] = "'SIN_ZONA'", ["IpAddress"] = "'10.0.0.1'" });
        await InsertAsync("Devices", new() { ["Id"] = "'CANCUN'", ["IpAddress"] = "'10.0.0.2'", ["TimeZoneId"] = "'America/Cancun'" });

        // Hora del reloj etiquetada como UTC, como las guardaba la versión anterior.
        await InsertLegacyPunchAsync("a0000000-0000-0000-0000-000000000001", "E1", "SIN_ZONA", "2026-10-27 09:01:59");
        await InsertLegacyPunchAsync("a0000000-0000-0000-0000-000000000002", "E1", "SIN_ZONA", "2022-06-15 09:00:00"); // horario de verano
        await InsertLegacyPunchAsync("a0000000-0000-0000-0000-000000000003", "E2", "CANCUN", "2026-10-27 09:00:00");
        // 09:00 pasa a 15:00Z mientras la de las 15:00 todavía no se convierte.
        await InsertLegacyPunchAsync("a0000000-0000-0000-0000-000000000004", "E3", "SIN_ZONA", "2026-10-26 09:00:00");
        await InsertLegacyPunchAsync("a0000000-0000-0000-0000-000000000005", "E3", "SIN_ZONA", "2026-10-26 15:00:00");

        await MigrateToAsync(Convert);

        (await PunchesAsync()).Should().Equal(
            "a0000000-0000-0000-0000-000000000001|2026-10-27 15:01:59|2026-10-27 09:01:59|America/Mexico_City|2026-10-27|",
            "a0000000-0000-0000-0000-000000000002|2022-06-15 14:00:00|2022-06-15 09:00:00|America/Mexico_City|2022-06-15|",
            "a0000000-0000-0000-0000-000000000003|2026-10-27 14:00:00|2026-10-27 09:00:00|America/Cancun|2026-10-27|",
            "a0000000-0000-0000-0000-000000000004|2026-10-26 15:00:00|2026-10-26 09:00:00|America/Mexico_City|2026-10-26|",
            "a0000000-0000-0000-0000-000000000005|2026-10-26 21:00:00|2026-10-26 15:00:00|America/Mexico_City|2026-10-26|");
    }

    [Fact]
    public async Task ALegacyPunchDownloadedAgainAfterTheUpgrade_IsVoided_NotDeleted()
    {
        if (!_available) return;
        await MigrateToAsync(Before);
        await InsertAsync("Devices", new() { ["Id"] = "'D1'", ["IpAddress"] = "'10.0.0.1'" });
        await InsertLegacyPunchAsync("b0000000-0000-0000-0000-000000000001", "E1", "D1", "2026-10-27 08:00:00");
        // La misma checada, descargada otra vez por la versión nueva.
        await InsertAsync("AttendanceRecords", new()
        {
            ["Id"] = "'b0000000-0000-0000-0000-000000000002'",
            ["EmployeeId"] = "'E1'",
            ["DeviceId"] = "'D1'",
            ["CheckTime"] = "timestamptz '2026-10-27 14:00:00+00'",
            ["OriginalCheckTime"] = "timestamptz '2026-10-27 08:00:00+00'",
            ["TimeZoneId"] = "'America/Mexico_City'",
            ["WorkDate"] = "date '2026-10-27'"
        });

        await MigrateToAsync(Convert);

        (await PunchesAsync()).Should().Equal(
            "b0000000-0000-0000-0000-000000000001|2026-10-27 14:00:00|2026-10-27 08:00:00|America/Mexico_City|2026-10-27|Copia de una checada registrada otra vez después de la actualización",
            "b0000000-0000-0000-0000-000000000002|2026-10-27 14:00:00|2026-10-27 08:00:00|America/Mexico_City|2026-10-27|");
    }

    [Fact]
    public async Task HireDatesThatEndedUpADayEarly_AreMovedBack_AndCorrectOnesAreLeftAlone()
    {
        if (!_available) return;
        await MigrateToAsync(Before);
        await InsertEmployeeAsync("E1", hireDate: "2025-03-10", terminationDate: "2025-12-31");
        await InsertEmployeeAsync("E2", hireDate: "2025-04-01");
        // E1 con las fechas que dejó la versión anterior en un servidor al oeste de UTC; E2 bien.
        await InsertAsync("EmployeeEmploymentPeriods", new() { ["Id"] = "gen_random_uuid()", ["EmployeeId"] = "'E1'", ["StartDate"] = "date '2025-03-09'", ["EndDate"] = "date '2025-12-30'" });
        await InsertAsync("EmployeeEmploymentPeriods", new() { ["Id"] = "gen_random_uuid()", ["EmployeeId"] = "'E2'", ["StartDate"] = "date '2025-04-01'" });
        await InsertAsync("EmployeeScheduleAssignments", new() { ["Id"] = "gen_random_uuid()", ["EmployeeId"] = "'E1'", ["EffectiveFrom"] = "date '2025-03-09'" });
        await InsertAsync("EmployeeScheduleAssignments", new() { ["Id"] = "gen_random_uuid()", ["EmployeeId"] = "'E2'", ["EffectiveFrom"] = "date '2025-04-01'" });

        await MigrateToAsync(Convert);

        (await QueryAsync(@"SELECT ""EmployeeId"" || '|' || ""StartDate"" || '|' || coalesce(""EndDate""::text, '') FROM ""EmployeeEmploymentPeriods"" ORDER BY 1"))
            .Should().Equal("E1|2025-03-10|2025-12-31", "E2|2025-04-01|");
        (await QueryAsync(@"SELECT ""EmployeeId"" || '|' || ""EffectiveFrom"" FROM ""EmployeeScheduleAssignments"" ORDER BY 1"))
            .Should().Equal("E1|2025-03-10", "E2|2025-04-01");
    }

    [Fact]
    public async Task UpgradingFromTheOldVersion_OnAServerWestOfUtc_KeepsTheHireDates()
    {
        if (!_available) return;
        // TerminationDate todavía no existía en v2.2.3.
        await MigrateToAsync(V223LastMigration);
        await InsertEmployeeAsync("E1", hireDate: "2025-03-10");

        // Hasta la anterior a ConvertLegacyCivilTimes, que si no repararía el error de las migraciones viejas.
        await MigrateToAsync(Before);

        (await QueryAsync(@"SELECT ""StartDate""::text FROM ""EmployeeEmploymentPeriods""")).Should().Equal("2025-03-10");
        (await QueryAsync(@"SELECT ""EffectiveFrom""::text FROM ""EmployeeScheduleAssignments""")).Should().Equal("2025-03-10");
    }

    private Task InsertLegacyPunchAsync(string id, string employee, string device, string clockTime) =>
        InsertAsync("AttendanceRecords", new()
        {
            ["Id"] = $"'{id}'",
            ["EmployeeId"] = $"'{employee}'",
            ["DeviceId"] = $"'{device}'",
            ["CheckTime"] = $"timestamptz '{clockTime}+00'"
        });

    private Task InsertEmployeeAsync(string id, string hireDate, string? terminationDate = null)
    {
        var values = new Dictionary<string, string> { ["Id"] = $"'{id}'", ["HireDate"] = $"timestamptz '{hireDate} 00:00+00'" };
        if (terminationDate != null) values["TerminationDate"] = $"timestamptz '{terminationDate} 00:00+00'";
        return InsertAsync("Employees", values);
    }

    /// <summary>Id|CheckTime UTC|OriginalCheckTime|zona|fecha laboral|motivo de anulación.</summary>
    private Task<List<string>> PunchesAsync() => QueryAsync(@"
        SELECT ""Id"" || '|' || to_char(""CheckTime"" AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS')
               || '|' || to_char(""OriginalCheckTime"" AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS')
               || '|' || ""TimeZoneId"" || '|' || ""WorkDate"" || '|' || coalesce(""VoidReason"", '')
          FROM ""AttendanceRecords"" ORDER BY ""Id""");

    /// <summary>
    /// Fila con el esquema del momento: cada columna obligatoria sin valor por defecto recibe un cero de su tipo, y sin
    /// revisar llaves foráneas (no se crean sucursales, horarios ni departamentos).
    /// </summary>
    private async Task InsertAsync(string table, Dictionary<string, string> values)
    {
        var required = await QueryAsync($@"
            SELECT column_name || '|' || CASE
                WHEN data_type IN ('integer','bigint','smallint','numeric','double precision','real') THEN '0'
                WHEN data_type = 'boolean' THEN 'false'
                WHEN data_type = 'uuid' THEN 'gen_random_uuid()'
                WHEN data_type = 'date' OR data_type LIKE 'timestamp%' THEN 'now()'
                WHEN data_type = 'interval' THEN 'interval ''0'''
                WHEN data_type LIKE 'time%' THEN 'time ''00:00'''
                ELSE '''x''' END
              FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = '{table}'
               AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'");
        foreach (var column in required.Select(r => r.Split('|', 2)))
        {
            values.TryAdd(column[0], column[1]);
        }

        await ExecuteAsync(_connectionString, $@"
            SET session_replication_role = replica;
            INSERT INTO ""{table}"" ({string.Join(", ", values.Keys.Select(k => $"\"{k}\""))})
            VALUES ({string.Join(", ", values.Values)});");
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

    private async Task MigrateToAsync(string? migration)
    {
        await using var context = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseNpgsql(_connectionString).Options,
            new Moq.Mock<MediatR.IPublisher>().Object);
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }
}
