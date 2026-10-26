using System;
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
/// Actualización desde v2.2.3 (última migración: AddExternalEmployees) con datos reales: marcajes descargados más de
/// una vez y enlazados a jornadas. Antes la reconciliación conservaba todas las copias enlazadas y la migración
/// automática fallaba al crear IX_AttendanceRecords_EmployeeId_CheckTime.
/// </summary>
public class PostgresUpgradeFromV223Tests
{
    private const string V223LastMigration = "20261003231726_AddExternalEmployees";

    [Fact]
    public async Task DuplicatedPunches_AreReconciled_AndDailiesPointToTheKeptOne()
    {
        var admin = AdminConnectionString();
        var database = $"attendance_upgrade_{Guid.NewGuid():N}";
        if (!await TryCreateDatabaseAsync(admin, database)) return;
        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;

        try
        {
            var options = new DbContextOptionsBuilder<AttendanceDbContext>().UseNpgsql(connectionString).Options;
            await using var context = new AttendanceDbContext(options, new Moq.Mock<MediatR.IPublisher>().Object);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(V223LastMigration);

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();
                // E1: la copia enlazada no es la primera del grupo. E2: las dos copias enlazadas. E3: triple, sin enlaces.
                await Execute(conn, @"
INSERT INTO ""AttendanceRecords"" VALUES
 ('00000000-0000-0000-0000-00000000000a','E1','D1','2026-09-01 15:00+00',1,0,1,NULL),
 ('00000000-0000-0000-0000-00000000000b','E1','D2','2026-09-01 15:00+00',1,0,1,NULL),
 ('00000000-0000-0000-0000-00000000000c','E2','D1','2026-09-01 15:00+00',1,0,1,NULL),
 ('00000000-0000-0000-0000-00000000000d','E2','D2','2026-09-01 15:00+00',1,0,1,NULL),
 ('00000000-0000-0000-0000-0000000000e1','E3','D1','2026-09-01 15:00+00',1,0,1,NULL),
 ('00000000-0000-0000-0000-0000000000e2','E3','D1','2026-09-01 15:00+00',1,0,1,NULL),
 ('00000000-0000-0000-0000-0000000000e3','E3','D2','2026-09-01 15:00+00',1,0,1,NULL);");
                await Execute(conn, $@"
INSERT INTO ""DailyAttendances"" (""Id"",""EmployeeId"",""Date"",""ToleranceMinutes"",""IsAbsent"",""LateMinutes"",""EarlyDepartureMinutes"",
 ""OvertimeMinutes"",""MissingCheckIn"",""MissingCheckOut"",""IsRestDay"",""WorkedOnRestDay"",""CalculateOvertimeBeforeEntry"",""OvertimeAuthorized"",
 ""HasTemporaryExits"",""LunchBreakMinutesApplied"",""TemporaryExitMinutes"",""TemporaryExitStatus"",""RoundingInterval"",""RoundingsEnabled"",
 ""MissingBlock1CheckOut"",""MissingBlock2CheckIn"",""IsAutoDetectedShift"",""CheckInRecordId"",""CheckOutRecordId"") VALUES
 {Daily("1", "E1", "2026-09-01", "'00000000-0000-0000-0000-00000000000b'", "NULL")},
 {Daily("2", "E2", "2026-09-01", "'00000000-0000-0000-0000-00000000000c'", "NULL")},
 {Daily("3", "E2", "2026-08-31", "NULL", "'00000000-0000-0000-0000-00000000000d'")};");
            }

            await migrator.MigrateAsync();

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();
                (await Scalar(conn, @"SELECT count(*) FROM ""AttendanceRecords""")).Should().Be(3, "queda un marcaje por empleado e instante");
                (await Scalar(conn, @"SELECT count(*) FROM ""AttendanceRecords"" WHERE ""Id"" = '00000000-0000-0000-0000-00000000000b'"))
                    .Should().Be(1, "se conserva la copia que ya usaba una jornada");
                (await Scalar(conn, @"SELECT count(*) FROM ""DailyAttendances"" d
                    WHERE (d.""CheckInRecordId"" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ""AttendanceRecords"" a WHERE a.""Id"" = d.""CheckInRecordId""))
                       OR (d.""CheckOutRecordId"" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ""AttendanceRecords"" a WHERE a.""Id"" = d.""CheckOutRecordId""))"))
                    .Should().Be(0, "ninguna jornada queda apuntando a una copia eliminada");
                (await Scalar(conn, @"SELECT count(DISTINCT coalesce(""CheckInRecordId"", ""CheckOutRecordId"")) FROM ""DailyAttendances"" WHERE ""EmployeeId"" = 'E2'"))
                    .Should().Be(1, "las dos jornadas de E2 apuntan al mismo marcaje conservado");
            }
        }
        finally
        {
            await DropDatabaseAsync(admin, database);
        }
    }

    private static string Daily(string n, string employee, string date, string checkIn, string checkOut) =>
        $"('10000000-0000-0000-0000-00000000000{n}','{employee}','{date}',10,false,0,0,0,false,false,false,false,false,false,false,0,0,0,0,false,false,false,false,{checkIn},{checkOut})";

    private static async Task Execute(NpgsqlConnection conn, string sql)
    {
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(NpgsqlConnection conn, string sql)
    {
        await using var command = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string AdminConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        var builder = new NpgsqlConnectionStringBuilder(string.IsNullOrWhiteSpace(env)
            ? "Host=localhost;Port=5432;Username=attendanceuser;Password=AttendancePass;"
            : env) { Database = "postgres" };
        return builder.ConnectionString;
    }

    private static async Task<bool> TryCreateDatabaseAsync(string admin, string database)
    {
        try
        {
            await using var conn = new NpgsqlConnection(admin);
            await conn.OpenAsync();
            await Execute(conn, $"CREATE DATABASE \"{database}\";");
            return true;
        }
        catch (Exception) when (!string.Equals(Environment.GetEnvironmentVariable("REQUIRE_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    private static async Task DropDatabaseAsync(string admin, string database)
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await Execute(conn, $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{database}';");
        await Execute(conn, $"DROP DATABASE IF EXISTS \"{database}\";");
    }
}
