extern alias zkteco;

using System.Collections.Concurrent;
using AttendanceSystem.Application.DTOs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using zkteco::AttendanceSystem.ZKTeco.Adapters;
using zkteco::AttendanceSystem.ZKTeco.Realtime;

namespace AttendanceSystem.Infrastructure.Tests.ZKTeco;

/// <summary>
/// Escucha en tiempo real del servicio ZKTeco: sesión fija por reloj, latido, reconexión y préstamo de la conexión.
/// El comportamiento del reloj real (eventos en MTA, sin OnDisConnected) está en tools/AttendanceSystem.ZKTeco.RealtimeProbe.
/// </summary>
public class ZKTecoRealtimeTests : IAsyncLifetime
{
    private static readonly RealtimeDeviceTarget Target = new("device-1", "10.0.0.5", 4370);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeClientFactory _factory = new();
    private readonly ZKTecoRealtimeChannel _channel;
    private readonly ZKTecoSessionManager _manager;
    private readonly CancellationTokenSource _readerCts = new();
    private readonly IAsyncEnumerator<RealtimeEvent> _events;

    public ZKTecoRealtimeTests()
    {
        var options = Options.Create(new ZKTecoRealtimeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            ReconnectDelays = [TimeSpan.FromMilliseconds(10)],
            ChannelCapacity = 100
        });
        _channel = new ZKTecoRealtimeChannel(options, NullLogger<ZKTecoRealtimeChannel>.Instance);
        _manager = new ZKTecoSessionManager(_factory, _channel, options, NullLoggerFactory.Instance, NullLogger<ZKTecoSessionManager>.Instance);
        _events = _channel.ReadAllAsync(_readerCts.Token).GetAsyncEnumerator();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _manager.DisposeAsync();
        _readerCts.Cancel();
        await _events.DisposeAsync();
    }

    [Fact]
    public async Task Listening_ConnectsRegistersEventsAndReportsFirstConnection()
    {
        var active = await _manager.SetRealtimeDevicesAsync([Target]);

        active.Should().Be(1);
        var connected = await NextConnectionAsync();
        connected.DeviceId.Should().Be("device-1");
        connected.Connection!.Connected.Should().BeTrue();
        connected.Connection.LastAliveUtc.Should().BeNull("es la primera conexión: no hay hueco que recuperar desde la escucha");
        _factory.Clients.Should().ContainSingle();
        _factory.Clients[0].ConnectCalls.Should().Be(1);
        _factory.Clients[0].EnableRealtimeCalls.Should().Be(1);
    }

    [Fact]
    public async Task Punches_ArePublishedOnce_AndInvalidOnesAreIgnored()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var client = _factory.Clients[0];
        var time = new DateTime(2026, 11, 3, 20, 25, 6);

        client.RaisePunch(new RealtimePunch("1821", time, 1, 0, 0));
        client.RaisePunch(new RealtimePunch("1821", time, 1, 0, 0));
        client.RaisePunch(new RealtimePunch("99", time, 1, 0, 0, IsInvalid: true));
        client.RaisePunch(new RealtimePunch("1821", time.AddSeconds(10), 1, 0, 0));

        var first = await NextPunchAsync();
        var second = await NextPunchAsync();
        first.Punch!.Should().Be(new RealtimePunch("1821", time, 1, 0, 0));
        second.Punch!.CheckTime.Should().Be(time.AddSeconds(10), "el duplicado y el inválido no se publican");
    }

    [Fact]
    public async Task FailedHeartbeat_ReportsDisconnection_AndReconnectsWithTheLastContact()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var client = _factory.Clients[0];

        client.HeartbeatResponds = false;
        var down = await NextConnectionAsync();
        down.Connection!.Connected.Should().BeFalse();
        down.Connection.LastAliveUtc.Should().NotBeNull();
        client.DisconnectCalls.Should().BeGreaterThan(0);

        client.HeartbeatResponds = true;
        var up = await NextConnectionAsync();
        up.Connection!.Connected.Should().BeTrue();
        up.Connection.LastAliveUtc.Should().Be(down.Connection.LastAliveUtc, "la descarga de respaldo empieza en el último contacto");
        client.EnableRealtimeCalls.Should().Be(2, "el registro de eventos se repite en cada conexión");
    }

    [Fact]
    public async Task FailedReconnection_IsRetriedUntilTheClockAnswers()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var client = _factory.Clients[0];

        client.ConnectSucceeds = false;
        client.HeartbeatResponds = false;
        (await NextConnectionAsync()).Connection!.Connected.Should().BeFalse();
        await WaitUntilAsync(() => client.ConnectCalls >= 3);

        client.ConnectSucceeds = true;
        client.HeartbeatResponds = true;
        (await NextConnectionAsync()).Connection!.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task Session_BorrowsTheListenerConnection_AndReturningItKeepsItOpen()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var listenerClient = _factory.Clients[0];

        var session = await _manager.ConnectSessionAsync("s1", Target.IpAddress, Target.Port);
        session.Should().BeSameAs(listenerClient, "un reloj escuchado no acepta abrir otra conexión en todos los firmwares");
        await _manager.DisconnectSessionAsync("s1");

        listenerClient.Disposed.Should().BeFalse();
        listenerClient.ConnectCalls.Should().Be(1);
        _factory.Clients.Should().ContainSingle();

        // Devolvió el candado de la IP: otra sesión puede tomarla.
        (await _manager.ConnectSessionAsync("s2", Target.IpAddress, Target.Port)).Should().BeSameAs(listenerClient);
        await _manager.DisconnectSessionAsync("s2");
    }

    [Fact]
    public async Task Heartbeat_IsSkippedWhileASessionHoldsTheConnection()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var client = _factory.Clients[0];
        await WaitUntilAsync(() => client.HeartbeatCalls > 0);

        await _manager.ConnectSessionAsync("s1", Target.IpAddress, Target.Port);
        await Task.Delay(100);
        var during = client.HeartbeatCalls;
        await Task.Delay(200);
        client.HeartbeatCalls.Should().Be(during);

        await _manager.DisconnectSessionAsync("s1");
        await WaitUntilAsync(() => client.HeartbeatCalls > during);
    }

    [Fact]
    public async Task Session_ReconnectsADroppedListenerConnection()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var client = _factory.Clients[0];
        client.ConnectSucceeds = false;
        client.HeartbeatResponds = false;
        (await NextConnectionAsync()).Connection!.Connected.Should().BeFalse();

        client.ConnectSucceeds = true;
        client.HeartbeatResponds = true;
        var session = await _manager.ConnectSessionAsync("s1", Target.IpAddress, Target.Port);

        session.Should().BeSameAs(client);
        client.IsConnected.Should().BeTrue();
        await _manager.DisconnectSessionAsync("s1");
    }

    [Fact]
    public async Task ClocksWithoutListening_StillGetTheirOwnConnection()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();

        var other = await _manager.ConnectSessionAsync("s1", "10.0.0.9", 4370);
        await _manager.DisconnectSessionAsync("s1");

        other.Should().NotBeSameAs(_factory.Clients[0]);
        _factory.Clients[1].Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task RemovingAClock_StopsItsListenerAndClosesTheConnection()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();
        var client = _factory.Clients[0];

        (await _manager.SetRealtimeDevicesAsync([])).Should().Be(0);

        client.Disposed.Should().BeTrue();
        var stopped = await NextConnectionAsync();
        stopped.Connection!.Connected.Should().BeFalse();
        stopped.Connection.Message.Should().Be("Escucha detenida");
        _manager.GetRealtimeStatuses().Should().BeEmpty();
    }

    [Fact]
    public async Task ChangingAClockAddress_RestartsItsListener()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();

        await _manager.SetRealtimeDevicesAsync([Target with { IpAddress = "10.0.0.6" }]);

        _factory.Clients[0].Disposed.Should().BeTrue();
        await WaitUntilAsync(() => _factory.Clients.Count == 2 && _factory.Clients[1].ConnectCalls == 1);
        _factory.Clients[1].LastIp.Should().Be("10.0.0.6");
    }

    [Fact]
    public async Task Statuses_ReportEachListenedClock()
    {
        await _manager.SetRealtimeDevicesAsync([Target]);
        await NextConnectionAsync();

        var status = _manager.GetRealtimeStatuses().Should().ContainSingle().Subject;
        status.DeviceId.Should().Be("device-1");
        status.Connection!.Connected.Should().BeTrue();
        status.Connection.LastAliveUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Channel_ANewSubscriberReplacesThePreviousOne()
    {
        var options = Options.Create(new ZKTecoRealtimeOptions { ChannelCapacity = 10 });
        var channel = new ZKTecoRealtimeChannel(options, NullLogger<ZKTecoRealtimeChannel>.Instance);
        await using var first = channel.ReadAllAsync().GetAsyncEnumerator();
        var firstMove = first.MoveNextAsync().AsTask();

        // El suscriptor se registra al empezar a leer.
        await using var second = channel.ReadAllAsync().GetAsyncEnumerator();
        var secondMove = second.MoveNextAsync().AsTask();
        (await firstMove.WaitAsync(Timeout)).Should().BeFalse("el suscriptor anterior termina para no procesar dos veces");

        channel.Publish(new RealtimeEvent("d", DateTime.UtcNow));
        (await secondMove.WaitAsync(Timeout)).Should().BeTrue();
    }

    [Fact]
    public async Task Channel_KeepsTheNewestEventsWhenFull()
    {
        var options = Options.Create(new ZKTecoRealtimeOptions { ChannelCapacity = 2 });
        var channel = new ZKTecoRealtimeChannel(options, NullLogger<ZKTecoRealtimeChannel>.Instance);
        foreach (var id in new[] { "a", "b", "c" }) channel.Publish(new RealtimeEvent(id, DateTime.UtcNow));

        await using var reader = channel.ReadAllAsync().GetAsyncEnumerator();
        var ids = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            await reader.MoveNextAsync().AsTask().WaitAsync(Timeout);
            ids.Add(reader.Current.DeviceId);
        }

        ids.Should().Equal("b", "c");
    }

    private async Task<RealtimeEvent> NextConnectionAsync()
    {
        while (true)
        {
            var next = await NextAsync();
            if (next.Connection != null) return next;
        }
    }

    private async Task<RealtimeEvent> NextPunchAsync()
    {
        while (true)
        {
            var next = await NextAsync();
            if (next.Punch != null) return next;
        }
    }

    private async Task<RealtimeEvent> NextAsync()
    {
        (await _events.MoveNextAsync().AsTask().WaitAsync(Timeout)).Should().BeTrue();
        return _events.Current;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeClientFactory : IZKTecoClientFactory
    {
        private readonly ConcurrentQueue<FakeClient> _created = new();

        public IReadOnlyList<FakeClient> Clients => _created.ToList();

        public IZKTecoClient Create()
        {
            var client = new FakeClient();
            _created.Enqueue(client);
            return client;
        }
    }

    private sealed class FakeClient : IZKTecoClient
    {
        private int _connectCalls, _enableCalls, _disconnectCalls, _heartbeatCalls;

        public volatile bool ConnectSucceeds = true;
        public volatile bool HeartbeatResponds = true;
        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public string? LastIp { get; private set; }
        public int ConnectCalls => Volatile.Read(ref _connectCalls);
        public int EnableRealtimeCalls => Volatile.Read(ref _enableCalls);
        public int DisconnectCalls => Volatile.Read(ref _disconnectCalls);
        public int HeartbeatCalls => Volatile.Read(ref _heartbeatCalls);

        public event EventHandler<RealtimePunch>? PunchReceived;

        public void RaisePunch(RealtimePunch punch) => PunchReceived?.Invoke(this, punch);

        public Task<bool> ConnectAsync(string ipAddress, int port, string? username = null, string? password = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectCalls);
            LastIp = ipAddress;
            IsConnected = ConnectSucceeds;
            return Task.FromResult(IsConnected);
        }

        public Task<bool> EnableRealtimeEventsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _enableCalls);
            return Task.FromResult(true);
        }

        public Task<DateTime?> GetDeviceTimeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _heartbeatCalls);
            return Task.FromResult<DateTime?>(HeartbeatResponds ? DateTime.Now : null);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _disconnectCalls);
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        public Task<DeviceLogsResult> GetAttendanceLogsAsync(string deviceId, DateTime? fromDate, DateTime? toDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ClearLogsAsync(string deviceId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetUserAsync(DeviceUserDto user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeviceUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
