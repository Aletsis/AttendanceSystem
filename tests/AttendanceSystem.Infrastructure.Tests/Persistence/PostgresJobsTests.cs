using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>Auditoría de tareas (bloques A y B) sobre PostgreSQL real.</summary>
public class PostgresJobsTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresJobsTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheJobLock_IsHeldAcrossConnections_UntilReleased()
    {
        if (!_fixture.IsAvailable) return;

        await using var contextA = _fixture.CreateDbContext();
        await using var contextB = _fixture.CreateDbContext();
        var lockA = new JobExclusionLock(contextA);
        var lockB = new JobExclusionLock(contextB);

        var held = await lockA.TryAcquireAsync("test-job", CancellationToken.None);
        held.Should().NotBeNull();
        (await lockB.TryAcquireAsync("test-job", CancellationToken.None)).Should().BeNull();

        await using (var other = await lockB.TryAcquireAsync("other-job", CancellationToken.None))
        {
            other.Should().NotBeNull("cada tarea tiene su propio candado");
        }

        await held!.DisposeAsync();
        await using var again = await lockB.TryAcquireAsync("test-job", CancellationToken.None);
        again.Should().NotBeNull();
    }

    [Fact]
    public async Task TheJobLock_IsReleased_WhenItsSessionDies()
    {
        if (!_fixture.IsAvailable) return;

        await using var context = _fixture.CreateDbContext();
        var held = await new JobExclusionLock(context).TryAcquireAsync("dying-job", CancellationToken.None);
        held.Should().NotBeNull();

        // Lo que hace una restauración (o la caída del proceso): la sesión que tiene el candado desaparece.
        await using (var admin = _fixture.CreateDbContext())
        {
            await admin.Database.ExecuteSqlRawAsync(
                $"SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND classid = {JobExclusionLock.LockNamespace}");
        }

        await using var other = _fixture.CreateDbContext();
        await using var again = await new JobExclusionLock(other).TryAcquireAsync("dying-job", CancellationToken.None);
        again.Should().NotBeNull();
        await held!.DisposeAsync();
    }

    [Fact]
    public async Task TheMonitor_PersistsStatusAndHardware_WithoutOverwritingWhatOthersSaved()
    {
        if (!_fixture.IsAvailable) return;

        var deviceId = $"MON-{Guid.NewGuid():N}"[..20];
        await using (var setup = _fixture.CreateDbContext())
        {
            var device = Device.Create(deviceId, "Reloj monitor", "10.0.0.9", 4370);
            device.MarkAsOnline();
            setup.Devices.Add(device);
            await setup.SaveChangesAsync();
        }

        await using var context = _fixture.CreateDbContext();
        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        client.Setup(c => c.GetDeviceInfoAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Mientras el monitor tiene el reloj cargado, otra operación guarda la última descarga.
                await using var concurrent = _fixture.CreateDbContext();
                var same = await concurrent.Devices.SingleAsync(d => d.Id == DeviceId.From(deviceId));
                same.UpdateLastDownloadAt(new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc));
                await concurrent.SaveChangesAsync();
                return new DeviceInfoDto("SN-MON", "Reloj monitor", "fw-2", "pl", 3, 3, 0, 95, 100, 100, 0, 100, "push", "sdk", "mac");
            });
        var factory = new Mock<IDeviceClientFactory>();
        factory.Setup(f => f.GetClient(It.IsAny<Device>())).Returns(client.Object);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(new SystemConfigurationDto(
                "Empresa", null, TimeSpan.Zero, TimeSpan.FromHours(8), false, false, null, false, AreAlertsEnabled: true)));
        var tracker = new Mock<IDeviceHeartbeatTracker>();
        tracker.SetupGet(t => t.TrackingSinceUtc).Returns(DateTime.UtcNow.AddHours(-1));

        var handler = new MonitorDeviceHealthCommandHandler(
            new DeviceRepository(context), factory.Object, new Mock<IEmailService>().Object, mediator.Object,
            NullLogger<MonitorDeviceHealthCommandHandler>.Instance, tracker.Object,
            new SystemAlertRepository(context), new DeviceLockService(), context);

        (await handler.Handle(new MonitorDeviceHealthCommand(), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await using var check = _fixture.CreateDbContext();
        var saved = await check.Devices.AsNoTracking().SingleAsync(d => d.Id == DeviceId.From(deviceId));
        saved.HardwareInfo!.AttendanceRecordCount.Should().Be(95);
        saved.HardwareInfo.FirmwareVersion.Should().Be("fw-2");
        saved.HardwareInfo.MacAddress.Should().Be("mac");
        saved.LastDownloadAt.Should().Be(new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc));
        (await check.SystemAlerts.AsNoTracking().CountAsync(a => a.Type == AlertType.DeviceMemoryFull && a.ReferenceId == deviceId && !a.IsResolved))
            .Should().Be(1);
    }

    [Fact]
    public async Task UnfinishedCalculations_AreCountedByTheDatesTheyTouch()
    {
        if (!_fixture.IsAvailable) return;

        await using (var context = _fixture.CreateDbContext())
        {
            var repository = new AttendanceConsolidationRepository(context);
            await repository.RequestAsync(EmployeeId.From("CIERRE-1"), new DateTime(2031, 3, 1), new DateTime(2031, 3, 5));
            await repository.RequestAsync(EmployeeId.From("CIERRE-2"), new DateTime(2031, 3, 20), new DateTime(2031, 3, 21));
        }

        await using var check = _fixture.CreateDbContext();
        var count = new AttendanceConsolidationRepository(check);
        (await count.CountUnfinishedInRangeAsync(new DateTime(2031, 3, 5), new DateTime(2031, 3, 11))).Should().Be(1, "toca el último día de la primera");
        (await count.CountUnfinishedInRangeAsync(new DateTime(2031, 3, 6), new DateTime(2031, 3, 12))).Should().Be(0);
        (await count.CountUnfinishedInRangeAsync(new DateTime(2031, 3, 1), new DateTime(2031, 3, 31))).Should().Be(2);

        var claims = await count.ClaimAsync("worker", 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1));
        foreach (var claim in claims.Where(c => c.EmployeeId == EmployeeId.From("CIERRE-1")))
            await count.CompleteAsync(claim.Id, "worker", claim.Version);
        (await count.CountUnfinishedInRangeAsync(new DateTime(2031, 3, 1), new DateTime(2031, 3, 11))).Should().Be(0, "ya se calculó");
    }
}
