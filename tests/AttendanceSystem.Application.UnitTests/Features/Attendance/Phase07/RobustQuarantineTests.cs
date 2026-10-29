using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.Features.Devices.Clock;
using AttendanceSystem.Application.Features.Devices.Quarantine;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 18: reglas de cuarentena según cómo llega el marcaje, memoria de cuarentena (una re-descarga no deshace lo
/// resuelto) e incidentes de reloj atrasado. Escenarios: reloj ADMS atrasado 12/24 h en tiempo real y reloj SDK
/// atrasado al descargar, y la re-descarga posterior de ambos.
/// </summary>
public class RobustQuarantineTests
{
    private const string Zone = "UTC";
    private static readonly DateTime Now = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0, DateTimeKind.Utc);
    private static DateTime Civil(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeHeartbeat : IDeviceHeartbeatTracker
    {
        public DateTime? ConnectedSince { get; set; }
        public void RecordActivity(string serialNumber, string? deviceId = null) { }
        public DateTime? GetLastActivity(string? serialNumber, string? deviceId = null) => Now;
        public bool IsDeviceOnline(string? serialNumber, string? deviceId = null, TimeSpan? timeout = null) => true;
        public DateTime? GetConnectedSince(string? serialNumber, string? deviceId = null) => ConnectedSince;
    }

    private sealed class FakeQuarantine : IQuarantinedPunchRepository
    {
        public List<QuarantinedPunch> Items { get; } = new();
        public Task AddRangeAsync(IEnumerable<QuarantinedPunch> punches, CancellationToken cancellationToken = default) { Items.AddRange(punches); return Task.CompletedTask; }
        public Task<QuarantinedPunch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<QuarantinedPunch>> GetAsync(QuarantineStatus? status, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QuarantinedPunch>>(Items.Where(p => status == null || p.Status == status).Take(take).ToList());
        public Task<IReadOnlySet<(string EmployeeId, DateTime ReportedTime)>> GetExistingAsync(DeviceId deviceId, IReadOnlyCollection<(string EmployeeId, DateTime ReportedTime)> keys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<(string, DateTime)>>(Items.Where(p => p.DeviceId == deviceId).Select(p => (p.EmployeeId, p.ReportedTime)).Where(keys.Contains).ToHashSet());
    }

    private sealed class FakeIncidents : IDeviceClockIncidentRepository
    {
        public List<DeviceClockIncident> Items { get; } = new();
        public Task<IReadOnlyList<DeviceClockIncident>> GetForDeviceAsync(DeviceId deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceClockIncident>>(Items.Where(i => i.DeviceId == deviceId).ToList());
        public Task AddAsync(DeviceClockIncident incident, CancellationToken cancellationToken = default) { Items.Add(incident); return Task.CompletedTask; }
    }

    private sealed class Rig
    {
        public AttendanceRecalculationHarness H { get; } = new();
        public FakeQuarantine Quarantine { get; } = new();
        public FakeIncidents Incidents { get; } = new();
        public FakeHeartbeat Heartbeat { get; } = new();
        public Device Device { get; } = Device.Create("DEV-Q18", "Reloj entrada", "10.0.0.5", 4370, timeZoneId: Zone);
        public PunchQuarantineService Service { get; }

        public Rig()
        {
            var devices = new Mock<IDeviceRepository>();
            devices.Setup(r => r.GetByIdAsync(It.IsAny<DeviceId>(), It.IsAny<CancellationToken>())).ReturnsAsync(Device);
            var configuration = new Mock<ISystemConfigurationRepository>();
            configuration.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SystemConfiguration.CreateDefault());
            Service = new PunchQuarantineService(Quarantine, H.UnitOfWork, DefaultTimeNormalizer.Instance, timeProvider: new FixedTime(),
                devices: devices.Object, configuration: configuration.Object, records: H.AttendanceRepository, incidents: Incidents, heartbeats: Heartbeat);
        }

        public void Ingested(string employee, DateTime civil) =>
            H.Store.Records.Add(AttendanceRecord.Create(EmployeeId.From(employee), Device.Id, DateTime.SpecifyKind(civil, DateTimeKind.Utc),
                VerifyMethod.Fingerprint, CheckType.CheckIn, originalCheckTime: Civil(civil), timeZoneId: Zone));

        public Task<QuarantineOutcome> EvaluateAsync(PunchArrivalContext context, params (string Employee, DateTime Civil)[] punches) =>
            Service.EvaluateAsync(Device.Id, Device.Name, punches.Select(p => new PunchCandidate(p.Employee, Civil(p.Civil), Zone, 1, 0, "ADMS")).ToList(), context);
    }

    private static readonly PunchArrivalContext Realtime = new(PunchArrival.Realtime, DeviceSerial: "SN-Q18");
    private static readonly PunchArrivalContext Redownload = new(PunchArrival.Redownload);

    // ---------- Escenario A: ADMS atrasado en tiempo real ----------

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    public async Task AnAdmsClockBehind_SendingInRealTime_GoesToQuarantineWithTheSuggestedCorrection(int hoursBehind)
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-3);
        rig.Ingested("100", Now.AddHours(-hoursBehind - 2)); // último marcaje bueno, antes del atraso

        var outcome = await rig.EvaluateAsync(Realtime, ("200", Now.AddHours(-hoursBehind)));

        outcome.Excluded.Should().Equal(0);
        outcome.LagQuarantined.Should().BeTrue();
        outcome.ClockBehind.Should().Be(TimeSpan.FromHours(hoursBehind));
        var punch = rig.Quarantine.Items.Single();
        punch.Rule.Should().Be(PunchQuarantineService.RuleRealtimeLag);
        punch.SuggestedOffsetMinutes.Should().Be(hoursBehind * 60);
    }

    [Fact]
    public async Task ABacklogAfterAnOutage_IsNotTreatedAsAClockBehind()
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddMinutes(-5); // el reloj acaba de reconectarse

        var outcome = await rig.EvaluateAsync(Realtime, ("200", Now.AddHours(-12)), ("201", Now.AddHours(-11)));

        outcome.Excluded.Should().BeEmpty();
        outcome.ClockBehind.Should().BeNull();
    }

    [Fact]
    public async Task ASmallRealtimeDrift_OnlyAlertsAndResyncs()
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-1);

        var outcome = await rig.EvaluateAsync(Realtime, ("200", Now.AddMinutes(-15)));

        outcome.Excluded.Should().BeEmpty("15 min está entre la alerta (10) y la cuarentena (30)");
        outcome.ClockBehind.Should().Be(TimeSpan.FromMinutes(15));
        outcome.LagQuarantined.Should().BeFalse();
    }

    // ---------- Escenario B: SDK atrasado al descargar ----------

    [Fact]
    public async Task AnSdkClockBehindAtDownload_QuarantinesTheNewPunchesWithTheSuggestedCorrection()
    {
        var rig = new Rig();
        var previousDownload = Now.AddHours(-30);
        rig.Ingested("100", previousDownload.AddHours(-1)); // ya descargado antes: contexto, no se toca
        var context = new PunchArrivalContext(PunchArrival.Download, TimeSpan.FromHours(-24), Civil(previousDownload));

        var outcome = await rig.EvaluateAsync(context,
            ("100", previousDownload.AddHours(-1)),
            ("200", Now.AddHours(-24 - 4)),  // checadas de hoy, grabadas como de ayer
            ("201", Now.AddHours(-24 - 1)));

        outcome.Excluded.Should().BeEquivalentTo(new[] { 1, 2 });
        rig.Quarantine.Items.Should().OnlyContain(p => p.Rule == PunchQuarantineService.RuleSdkBehind && p.SuggestedOffsetMinutes == 1440);
    }

    [Fact]
    public async Task AnSdkClockAheadAtDownload_QuarantinesWithANegativeCorrection()
    {
        var rig = new Rig();
        var context = new PunchArrivalContext(PunchArrival.Download, TimeSpan.FromHours(2), Civil(Now.AddHours(-30)));

        var outcome = await rig.EvaluateAsync(context,
            ("200", Now.AddHours(-3)),  // checada de hace 5 h, grabada 2 h adelante
            ("201", Now.AddHours(1)));  // checada de hace 1 h: quedó en el futuro

        outcome.Excluded.Should().BeEquivalentTo(new[] { 0, 1 });
        rig.Quarantine.Items.Select(p => (p.EmployeeId, p.Rule, p.SuggestedOffsetMinutes)).Should().BeEquivalentTo(new[]
        {
            ("200", PunchQuarantineService.RuleSdkAhead, (int?)-120),
            ("201", PunchQuarantineService.RuleFuture, (int?)-120),
        });
    }

    [Fact]
    public async Task APunchFromTheFutureInRealTime_SuggestsHowFarAheadTheClockIs()
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-1);

        await rig.EvaluateAsync(Realtime, ("200", Now.AddMinutes(45)));

        rig.Quarantine.Items.Single().Should().Match<QuarantinedPunch>(p => p.Rule == PunchQuarantineService.RuleFuture && p.SuggestedOffsetMinutes == -45);
    }

    [Theory]
    [InlineData(1, null)]  // dentro de la tolerancia de sincronización: nada
    [InlineData(5, 5)]     // pasa la tolerancia: se resincroniza (sin alerta, menos de 10)
    public async Task ARealtimeDriftAboveTheSyncTolerance_Resyncs(int minutesBehind, int? expectedBehind)
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-1);

        var outcome = await rig.EvaluateAsync(Realtime, ("200", Now.AddMinutes(-minutesBehind)));

        outcome.Excluded.Should().BeEmpty();
        outcome.ClockBehind.Should().Be(expectedBehind is { } m ? TimeSpan.FromMinutes(m) : null);
    }

    // ---------- Descarga SDK: escalones del desfase ----------

    [Theory]
    [InlineData(-5, false, false)]  // sólo se resincroniza
    [InlineData(-15, true, false)]  // alerta; los marcajes entran
    [InlineData(-45, true, true)]   // cuarentena
    [InlineData(45, true, true)]    // adelantado: también cuarentena
    public async Task AnSdkDownload_HandlesTheClockDriftByTier(int driftMinutes, bool alerts, bool quarantines)
    {
        var rig = new Rig();
        var previousDownload = Now.AddHours(-6);
        rig.Device.UpdateLastDownloadAt(previousDownload);
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetByIdAsync(It.IsAny<DeviceId>(), It.IsAny<CancellationToken>())).ReturnsAsync(rig.Device);

        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        client.Setup(c => c.GetDeviceTimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Civil(Now.AddMinutes(driftMinutes)));
        client.Setup(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        DateTime? askedFrom = null;
        client.Setup(c => c.GetAttendanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTime?, DateTime?, CancellationToken>((_, from, _, _) => askedFrom = from)
            .ReturnsAsync(DeviceLogsResult.Empty());
        var factory = new Mock<IDeviceClientFactory>();
        factory.Setup(f => f.GetClient(It.IsAny<DeviceBrand>())).Returns(client.Object);
        var resolver = new Mock<IAuthoritativeTimeZoneResolver>();
        resolver.Setup(r => r.ResolveForDeviceAsync(It.IsAny<DeviceId?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Zone);
        var locks = new Mock<IDeviceLockService>();
        locks.Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((_, action, _) => action());
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Domain.Aggregates.BranchAggregate.Branch>());
        var alertRepository = new Mock<ISystemAlertRepository>();
        var clock = new DeviceClockService(DefaultTimeNormalizer.Instance, resolver.Object, NullLogger<DeviceClockService>.Instance, new FixedTime(),
            alertRepository.Object, rig.H.UnitOfWork);

        var handler = new DownloadFromDeviceCommandHandler(
            devices.Object, rig.H.AttendanceRepository, Mock.Of<IDownloadLogRepository>(), factory.Object, rig.H.UnitOfWork,
            new AttendanceDeduplicationService(), NullLogger<DownloadFromDeviceCommandHandler>.Instance, Mock.Of<IMediator>(), locks.Object,
            Mock.Of<IAdmsCommandService>(), branches.Object, Mock.Of<IAttendanceJobScheduler>(),
            Mock.Of<IEmployeeRepository>(), Mock.Of<IExternalAttendanceLogRepository>(), DefaultTimeNormalizer.Instance, resolver.Object,
            clock: clock, quarantine: rig.Service);

        await handler.Handle(new DownloadFromDeviceCommand(rig.Device.Id.Value), CancellationToken.None);

        client.Verify(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        alertRepository.Verify(a => a.AddAsync(It.IsAny<SystemAlert>(), It.IsAny<CancellationToken>()), alerts ? Times.Once() : Times.Never());
        rig.Incidents.Items.Should().HaveCount(quarantines ? 1 : 0);
        var behind = quarantines && driftMinutes < 0 ? TimeSpan.FromMinutes(-driftMinutes) + TimeSpan.FromDays(1) : TimeSpan.Zero;
        askedFrom.Should().Be(Civil(previousDownload) - behind, "sólo un reloj atrasado en cuarentena obliga a leer más atrás");
        if (quarantines) rig.Incidents.Items.Single().OffsetMinutes.Should().Be(-driftMinutes);
    }

    // ---------- Re-descargas ----------

    [Fact]
    public async Task ARedownload_DoesNotUndoWhatTheQuarantineResolved()
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-3);
        await rig.EvaluateAsync(Realtime, ("200", Now.AddHours(-24)));
        rig.Quarantine.Items.Single().Discard("admin", "No era real");

        // Ya con el reloj en hora, una re-descarga trae el mismo marcaje con la hora mal.
        var outcome = await rig.EvaluateAsync(Redownload, ("200", Now.AddHours(-24)));

        outcome.Excluded.Should().Equal(0);
        outcome.AlreadyResolved.Should().Be(1);
        rig.Quarantine.Items.Should().HaveCount(1, "no se vuelve a poner en cuarentena");
    }

    [Fact]
    public async Task APunchNeverSent_FromThePeriodTheClockWasBehind_IsQuarantinedOnRedownload()
    {
        var rig = new Rig();
        await rig.Service.RecordClockIncidentAsync(rig.Device.Id, Civil(Now.AddHours(-30)), Civil(Now.AddHours(-23)), TimeSpan.FromHours(24), "Reloj atrasado", CancellationToken.None);

        var outcome = await rig.EvaluateAsync(Redownload, ("300", Now.AddHours(-26)), ("301", Now.AddHours(-2)));

        outcome.Excluded.Should().Equal(0);
        rig.Quarantine.Items.Single().Should().Match<QuarantinedPunch>(p => p.Rule == PunchQuarantineService.RuleIncident && p.SuggestedOffsetMinutes == 1440);
    }

    [Fact]
    public async Task OnARedownload_OldPunchesAreNormal_ButTheClockRulesStillApply()
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-30);

        var outcome = await rig.EvaluateAsync(Redownload,
            ("400", Now.AddDays(-10)),
            ("401", Now.AddDays(-10).AddMinutes(5)),
            ("402", Now.AddDays(-10).AddMinutes(-90)),  // la hora retrocedió 95 min: salto
            ("403", new DateTime(2000, 1, 1, 8, 0, 0)));  // el reloj perdió su fecha (antes se aceptaba el 01/01/2000)

        outcome.Excluded.Should().BeEquivalentTo(new[] { 2, 3 });
        rig.Quarantine.Items.Select(p => p.Rule).Should().BeEquivalentTo(new[] { PunchQuarantineService.RuleBackwardJump, PunchQuarantineService.RuleMinDate });
    }

    [Fact]
    public async Task ADeviceMigratedWithItsHistory_AcceptsPunchesFromTheDateTheAdministratorSets()
    {
        var rig = new Rig();
        (await rig.EvaluateAsync(Redownload, ("500", Now.AddDays(-200)))).Excluded.Should().Equal(0);

        rig.Device.SetAcceptPunchesFrom(Now.AddDays(-365));
        (await rig.EvaluateAsync(Redownload, ("501", Now.AddDays(-200)))).Excluded.Should().BeEmpty();
    }

    // ---------- Liberación ----------

    [Fact]
    public async Task Releasing_ValidatesTheCorrectedTimeAgainstTheConfiguredMinimumDate()
    {
        var rig = new Rig();
        rig.Device.SetAcceptPunchesFrom(Now.AddDays(-10));
        await rig.EvaluateAsync(Redownload, ("600", Now.AddDays(-20)));
        var punch = rig.Quarantine.Items.Single();
        var handler = new QuarantinedPunchDecisionHandler(rig.Quarantine, rig.H.AttendanceRepository, rig.H.UnitOfWork, DefaultTimeNormalizer.Instance, rig.Service);

        var tooEarly = await handler.Handle(new ReleaseQuarantinedPunchCommand(punch.Id, 60, "Corregido"), CancellationToken.None);
        tooEarly.IsFailure.Should().BeTrue("antes se aceptaba cualquier fecha posterior al año 2000");
        tooEarly.Error.Should().Contain("fecha mínima");

        var released = await handler.Handle(new ReleaseQuarantinedPunchCommand(punch.Id, 15 * 1440, "Corregido"), CancellationToken.None);
        released.IsSuccess.Should().BeTrue();
    }

    // ---------- Liberación en bloque ----------

    [Fact]
    public async Task AllPendingPunchesOfADevice_AreReleasedWithTheirSuggestedCorrection()
    {
        var rig = new Rig();
        rig.Heartbeat.ConnectedSince = Now.AddDays(-3);
        await rig.EvaluateAsync(Realtime, ("200", Now.AddHours(-12)));
        await rig.EvaluateAsync(Realtime, ("201", Now.AddHours(-12).AddMinutes(10)));
        rig.Quarantine.Items.Should().HaveCount(2);
        var handler = new QuarantinedPunchDecisionHandler(rig.Quarantine, rig.H.AttendanceRepository, rig.H.UnitOfWork, DefaultTimeNormalizer.Instance, rig.Service);

        var result = await handler.Handle(new ReleaseQuarantinedPunchesCommand(rig.Device.Id.Value, null, "Reloj atrasado 12 h"), CancellationToken.None);

        result.Value.Released.Should().Be(2);
        rig.Quarantine.Items.Should().OnlyContain(p => p.Status == QuarantineStatus.Released);
        rig.Quarantine.Items.Should().OnlyContain(p => p.ReleasedTime == p.ReportedTime.AddMinutes(p.SuggestedOffsetMinutes!.Value),
            "cada marcaje se corrige con el atraso que se le sugirió");
        rig.H.Store.Records.Select(r => r.GetCivilTime()).Should().BeEquivalentTo(rig.Quarantine.Items.Select(p => p.ReleasedTime!.Value));
    }
}
