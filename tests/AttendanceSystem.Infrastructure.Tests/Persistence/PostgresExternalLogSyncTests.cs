using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Infrastructure.Common;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Checadas de sucursales externas: subida y descarga contra la nube. La BD de prueba hace también de nube
/// (su tabla external_attendance_logs es distinta de la local "ExternalAttendanceLogs").
/// </summary>
public class PostgresExternalLogSyncTests : IClassFixture<PostgresTestDatabaseFixture>, IDisposable
{
    private static readonly DateTime Eight = new(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc);
    private readonly PostgresTestDatabaseFixture _fixture;
    private readonly CloudDataSourceProvider _dataSources = new();

    public PostgresExternalLogSyncTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose() => _dataSources.Dispose();

    private Mock<ISystemConfigurationRepository> Config(string? connectionString)
    {
        var config = SystemConfiguration.CreateDefault();
        config.UpdateCloudSyncSettings(true, connectionString);
        var repo = new Mock<ISystemConfigurationRepository>();
        repo.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(config);
        return repo;
    }

    private LogTransferService Transfer(AttendanceDbContext context, string? cloudConnectionString = null) => new(
        Config(cloudConnectionString ?? _fixture.ConnectionString).Object,
        new ExternalAttendanceLogRepository(context),
        new UnitOfWork(context),
        DefaultTimeNormalizer.Instance,
        _dataSources,
        NullLogger<LogTransferService>.Instance);

    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateDbContext();
        await context.ExternalAttendanceLogs.ExecuteDeleteAsync();
        // Crea la tabla de la nube (si falta) y la deja vacía.
        await Transfer(context).TestConnectionAsync();
        await ExecuteAsync("TRUNCATE external_attendance_logs;");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task SeedLocalAsync(params ExternalAttendanceLog[] logs)
    {
        await using var context = _fixture.CreateDbContext();
        context.ExternalAttendanceLogs.AddRange(logs);
        await context.SaveChangesAsync();
    }

    private static ExternalAttendanceLog Log(string employee, DateTime checkTime) =>
        ExternalAttendanceLog.Create("B01", employee, checkTime, 1, 0, "SN1");

    [Fact]
    public async Task Upload_SamePunchWithAnotherId_IsIgnoredByTheCloud_AndBothAreMarkedTransferred()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        // Una redescarga anterior a esta corrección dejó la misma checada dos veces con distinto id.
        await SeedLocalAsync(Log("123", Eight), Log("123", Eight), Log("123", Eight.AddHours(9)));

        await using var context = _fixture.CreateDbContext();
        var result = await Transfer(context).RetryPendingTransfersAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
        (await ScalarAsync("SELECT COUNT(*) FROM external_attendance_logs")).Should().Be(2);

        await using var check = _fixture.CreateDbContext();
        (await check.ExternalAttendanceLogs.ToListAsync()).Should().OnlyContain(l => l.Status == ExternalLogStatus.Transferred);
    }

    [Fact]
    public async Task Upload_NewLogsGoFirst_EvenWhenOlderOnesKeepFailing()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var stuck = Enumerable.Range(0, 100).Select(i =>
        {
            var log = Log($"9{i}", Eight.AddMinutes(-i));
            log.MarkAsFailed("rechazado");
            return log;
        }).ToArray();
        await SeedLocalAsync(stuck);
        var fresh = Log("123", Eight.AddDays(1));
        await SeedLocalAsync(fresh);

        await using var context = _fixture.CreateDbContext();
        var batch = await new ExternalAttendanceLogRepository(context).GetPendingOrFailedLogsAsync(100);

        batch.Select(l => l.Id).Should().Contain(fresh.Id, "un log sin intentos no espera detrás de 100 fallidos");
    }

    [Fact]
    public async Task Upload_WhenTheCloudIsUnreachable_LeavesLogsUntouched()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();
        await SeedLocalAsync(Log("123", Eight));

        var unreachable = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Port = 1, Timeout = 2 }.ConnectionString;
        await using var context = _fixture.CreateDbContext();
        var result = await Transfer(context, unreachable).RetryPendingTransfersAsync();

        result.IsSuccess.Should().BeFalse();
        await using var check = _fixture.CreateDbContext();
        var log = await check.ExternalAttendanceLogs.SingleAsync();
        log.Status.Should().Be(ExternalLogStatus.Pending);
        log.RetryCount.Should().Be(0, "una caída de la nube no es un intento fallido del registro");
    }

    [Fact]
    public async Task ExcludeAlreadyStored_DropsStoredPunches_AndRepeatsWithinTheBatch()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();
        await SeedLocalAsync(Log("123", Eight));

        await using var context = _fixture.CreateDbContext();
        var candidates = new[] { Log("123", Eight), Log("123", Eight.AddHours(9)), Log("123", Eight.AddHours(9)), Log("456", Eight) };
        var fresh = await new ExternalAttendanceLogRepository(context).ExcludeAlreadyStoredAsync(candidates);

        fresh.Select(l => (l.EmployeeId, l.CheckTime)).Should().Equal(("123", Eight.AddHours(9)), ("456", Eight));
    }

    [Fact]
    public async Task Download_FailedLogIsReleasedWithAnAttempt_AndNoLongerBlocksNewOnes()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();
        await ExecuteAsync($@"
            INSERT INTO external_attendance_logs (id, branch_code, employee_id, check_time, verify_method, check_type)
            VALUES ('{Guid.NewGuid()}', 'A01', 'BAD', '2026-10-01 13:00:00+00', 1, 0),
                   ('{Guid.NewGuid()}', 'A01', 'OK',  '2026-10-01 14:00:00+00', 1, 0);");

        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.Is<RecordAttendanceCommand>(c => c.EmployeeId == "BAD"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("No se pudo registrar el marcaje (Transient): caído"));
        sender.Setup(s => s.Send(It.Is<RecordAttendanceCommand>(c => c.EmployeeId == "OK"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));
        var sync = new CloudLogSyncService(Config(_fixture.ConnectionString).Object, sender.Object, _dataSources, NullLogger<CloudLogSyncService>.Instance);

        var result = await sync.SyncPendingLogsForBranchesAsync(new[] { "A01" });

        result.Value.Should().Be(1);
        (await ScalarAsync("SELECT download_attempts FROM external_attendance_logs WHERE employee_id = 'BAD' AND is_downloaded = FALSE AND claim_token IS NULL"))
            .Should().Be(1, "queda pendiente, sin reclamo y con un intento más");

        // Con un lote entero (200) de fallidos más antiguos, un log nuevo posterior entra igual en el lote.
        await ExecuteAsync(@"
            INSERT INTO external_attendance_logs (id, branch_code, employee_id, check_time, verify_method, check_type, download_attempts)
            SELECT gen_random_uuid(), 'A01', 'STUCK' || n, TIMESTAMPTZ '2026-09-01 00:00:00+00' + n * INTERVAL '1 minute', 1, 0, 3
            FROM generate_series(1, 200) AS n;");
        await ExecuteAsync($@"
            INSERT INTO external_attendance_logs (id, branch_code, employee_id, check_time, verify_method, check_type)
            VALUES ('{Guid.NewGuid()}', 'A01', 'NEW', '2026-10-01 15:00:00+00', 1, 0);");
        var next = await sync.FetchPendingLogsAsync("A01");
        next.Value.Select(l => l.EmployeeId).Should().Contain(new[] { "NEW", "BAD" });
    }
}
