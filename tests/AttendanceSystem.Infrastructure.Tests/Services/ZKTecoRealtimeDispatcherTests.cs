using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordRealtimePunches;
using AttendanceSystem.Infrastructure.Services.ZKTecoRealtime;
using AttendanceSystem.ZKTeco.Grpc;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

/// <summary>
/// Lo que llega del stream de tiempo real: lotes de marcajes por reloj, descarga de respaldo y actividad de los relojes.
/// </summary>
public class ZKTecoRealtimeDispatcherTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IAttendanceJobScheduler> _scheduler = new();
    private readonly Mock<IDeviceHeartbeatTracker> _heartbeats = new();
    private readonly ManualTime _time = new();
    private readonly List<RecordRealtimePunchesCommand> _sent = new();
    private readonly ZKTecoRealtimeDispatcher _dispatcher;

    public ZKTecoRealtimeDispatcherTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RecordRealtimePunchesCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<int>>, CancellationToken>((c, _) => _sent.Add((RecordRealtimePunchesCommand)c))
            .ReturnsAsync(Result<int>.Success(1));
        var services = new ServiceCollection()
            .AddSingleton(_mediator.Object)
            .AddSingleton(_scheduler.Object)
            .BuildServiceProvider();
        _dispatcher = new ZKTecoRealtimeDispatcher(services.GetRequiredService<IServiceScopeFactory>(), _heartbeats.Object,
            NullLogger<ZKTecoRealtimeDispatcher>.Instance, _time);
    }

    [Fact]
    public async Task Punches_AreSavedInOneBatchPerClock_WithTheClockCivilTime()
    {
        _dispatcher.Handle(Punch("d1", "1821", "2026-11-03T20:25:06.0000000"));
        _dispatcher.Handle(Punch("d1", "1822", "2026-11-03T20:25:16.0000000"));
        _dispatcher.Handle(Punch("d2", "7", "2026-11-03T20:26:00.0000000"));

        await _dispatcher.FlushAsync(CancellationToken.None);

        _sent.Should().HaveCount(2);
        var first = _sent.Single(c => c.DeviceId == "d1");
        first.Punches.Select(p => p.UserId).Should().Equal("1821", "1822");
        first.Punches[0].CheckTime.Should().Be(new DateTime(2026, 11, 3, 20, 25, 6));
        first.Punches[0].CheckTime.Kind.Should().Be(DateTimeKind.Unspecified, "es la hora que marca el reloj, no un instante");

        await _dispatcher.FlushAsync(CancellationToken.None);
        _sent.Should().HaveCount(2, "lo guardado no se vuelve a enviar");
    }

    [Fact]
    public async Task UnreadableTime_IsDropped()
    {
        _dispatcher.Handle(Punch("d1", "1821", "no-es-fecha"));

        await _dispatcher.FlushAsync(CancellationToken.None);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public void Connection_AsksForOneCatchUpDownload_UntilTheCooldownPasses()
    {
        _dispatcher.Handle(Connection("d1", connected: true));
        _dispatcher.Handle(Connection("d1", connected: false));
        _dispatcher.Handle(Connection("d1", connected: true));
        _scheduler.Verify(s => s.EnqueueDeviceCatchUp("d1"), Times.Once, "una red inestable no debe encolar una descarga por reconexión");

        _time.Advance(ZKTecoRealtimeDispatcher.CatchUpCooldown);
        _dispatcher.Handle(Connection("d1", connected: true));
        _scheduler.Verify(s => s.EnqueueDeviceCatchUp("d1"), Times.Exactly(2));
    }

    [Fact]
    public async Task FailedBatch_AsksForACatchUpDownloadEvenDuringTheCooldown()
    {
        _dispatcher.Handle(Connection("d1", connected: true));
        _mediator.Setup(m => m.Send(It.IsAny<RecordRealtimePunchesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure("base de datos no disponible"));
        _dispatcher.Handle(Punch("d1", "1821", "2026-11-03T20:25:06"));

        await _dispatcher.FlushAsync(CancellationToken.None);

        _scheduler.Verify(s => s.EnqueueDeviceCatchUp("d1"), Times.Exactly(2), "el marcaje no guardado sólo lo recupera la descarga");
    }

    [Fact]
    public async Task BatchThatThrows_AsksForACatchUpDownload()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RecordRealtimePunchesCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("restauración en curso"));
        _dispatcher.Handle(Punch("d1", "1821", "2026-11-03T20:25:06"));

        await _dispatcher.FlushAsync(CancellationToken.None);

        _scheduler.Verify(s => s.EnqueueDeviceCatchUp("d1"), Times.Once);
    }

    [Fact]
    public void KeepAlive_RecordsActivityOnlyForConnectedClocks()
    {
        _dispatcher.Handle(Connection("d1", connected: true));
        _dispatcher.Handle(Connection("d2", connected: true));
        _dispatcher.Handle(Connection("d2", connected: false));
        _heartbeats.Invocations.Clear();

        _dispatcher.KeepAlive();

        _heartbeats.Verify(h => h.RecordActivity(It.IsAny<string>(), "d1"), Times.Once);
        _heartbeats.Verify(h => h.RecordActivity(It.IsAny<string>(), "d2"), Times.Never);
    }

    private static RealtimeUpdate Punch(string deviceId, string userId, string checkTime) => new()
    {
        DeviceId = deviceId,
        OccurredAtUtc = DateTime.UtcNow.ToString("o"),
        Punch = new RealtimePunchRecord { UserId = userId, CheckTime = checkTime, VerifyMode = 1, InOutMode = 0 }
    };

    private static RealtimeUpdate Connection(string deviceId, bool connected) => new()
    {
        DeviceId = deviceId,
        OccurredAtUtc = DateTime.UtcNow.ToString("o"),
        Connection = new RealtimeConnectionState { Connected = connected, Message = connected ? "Conectado" : "Sin conexión" }
    };

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
