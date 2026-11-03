using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

/// <summary>
/// Auditoría de tareas, bloque B: el monitor guarda el estado y avisa una vez por cambio (desconexión, regreso y
/// memoria llena), no en cada pasada; no compite por la conexión de un reloj en uso.
/// </summary>
public class MonitorDeviceHealthCommandHandlerTests
{
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IDeviceClientFactory> _clients = new();
    private readonly Mock<IDeviceClient> _client = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IDeviceHeartbeatTracker> _tracker = new();
    private readonly Mock<ISystemAlertRepository> _alertRepository = new();
    private readonly Mock<IDeviceLockService> _locks = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<SystemAlert> _alerts = new();
    private readonly List<string> _sent = new();
    private readonly List<string> _calls = new();
    private bool _deviceBusy;

    public MonitorDeviceHealthCommandHandlerTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(new SystemConfigurationDto(
                "Empresa", null, TimeSpan.Zero, TimeSpan.FromHours(8), false, false, null, false,
                AreAlertsEnabled: true, SystemFailureAlertEmails: "ti@empresa.com")));
        _alertRepository.Setup(r => r.GetActiveAlertsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _alerts.Where(a => !a.IsResolved).ToList());
        _alertRepository.Setup(r => r.AddAsync(It.IsAny<SystemAlert>(), It.IsAny<CancellationToken>()))
            .Callback<SystemAlert, CancellationToken>((a, _) => _alerts.Add(a))
            .Returns(Task.CompletedTask);
        _email.Setup(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), AlertLevel.SystemFailure, It.IsAny<CancellationToken>()))
            .Callback<string, string, AlertLevel, CancellationToken>((subject, _, _, _) => { _sent.Add(subject); _calls.Add("email"); })
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("save"))
            .ReturnsAsync(1);
        _tracker.SetupGet(t => t.TrackingSinceUtc).Returns(DateTime.UtcNow.AddHours(-1));
        _clients.Setup(f => f.GetClient(It.IsAny<Device>())).Returns(_client.Object);
        _locks.Setup(l => l.TryExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>(async (_, action, _) =>
            {
                if (_deviceBusy) return false;
                await action();
                return true;
            });
    }

    private MonitorDeviceHealthCommandHandler Handler() => new(
        _devices.Object, _clients.Object, _email.Object, _mediator.Object,
        NullLogger<MonitorDeviceHealthCommandHandler>.Instance, _tracker.Object,
        _alertRepository.Object, _locks.Object, _unitOfWork.Object);

    private Task<Result<int>> RunAsync() => Handler().Handle(new MonitorDeviceHealthCommand(), CancellationToken.None);

    private void Devices(params Device[] devices) =>
        _devices.Setup(r => r.GetAllDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(devices);

    private static Device Adms() =>
        Device.Create("ADMS1", "Reloj ADMS", "10.0.0.2", 4370, downloadMethod: DeviceDownloadMethod.Adms, serialNumber: "SN-1");

    private static Device Sdk() => Device.Create("SDK1", "Reloj SDK", "10.0.0.3", 4370);

    private void AdmsOnline(bool online) =>
        _tracker.Setup(t => t.IsDeviceOnline(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan?>())).Returns(online);

    private void SdkResponds(bool connected, int records = 0, int capacity = 0)
    {
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connected);
        _client.Setup(c => c.GetDeviceInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceInfoDto("SN-3", "Reloj SDK", "fw", "pl", 1, 1, 0, records, 100, 100, 0, capacity, "push", "sdk", "mac"));
    }

    [Fact]
    public async Task AnOfflineClock_IsAlertedOnce_NotOnEveryRun()
    {
        var device = Adms();
        device.MarkAsOnline();
        Devices(device);
        AdmsOnline(false);

        (await RunAsync()).IsSuccess.Should().BeTrue();
        (await RunAsync()).IsSuccess.Should().BeTrue();
        (await RunAsync()).IsSuccess.Should().BeTrue();

        device.Status.Should().Be(DeviceStatus.Offline);
        _sent.Should().ContainSingle().Which.Should().Contain("Offline");
        _alerts.Should().ContainSingle(a => a.Type == AlertType.DeviceOffline && !a.IsResolved);
    }

    [Fact]
    public async Task TheStatusIsSaved_BeforeTheEmailsAreSent()
    {
        Devices(Adms());
        AdmsOnline(false);

        await RunAsync();

        _calls.Should().Equal("save", "email");
    }

    [Fact]
    public async Task AClockThatComesBack_ResolvesTheAlert_AndNotifiesOnce()
    {
        var device = Adms();
        Devices(device);
        AdmsOnline(false);
        await RunAsync();

        // El controlador ADMS lo marca en línea al recibir su latido; el aviso abierto es lo que detecta el regreso.
        device.MarkAsOnline();
        AdmsOnline(true);
        await RunAsync();
        await RunAsync();

        device.Status.Should().Be(DeviceStatus.Online);
        _sent.Should().HaveCount(2);
        _sent[1].Should().Contain("en línea de nuevo");
        _alerts.Should().OnlyContain(a => a.IsResolved);
    }

    [Fact]
    public async Task RightAfterStartup_AnAdmsClockWithoutHeartbeats_IsNotMarkedOffline()
    {
        var device = Adms();
        device.MarkAsOnline();
        Devices(device);
        AdmsOnline(false);
        _tracker.SetupGet(t => t.TrackingSinceUtc).Returns(DateTime.UtcNow);

        await RunAsync();

        device.Status.Should().Be(DeviceStatus.Online);
        _sent.Should().BeEmpty();
        _alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task FullMemory_IsAlertedOnce_AndAgainOnlyAfterItWasCleared()
    {
        var device = Sdk();
        Devices(device);
        SdkResponds(true, records: 95, capacity: 100);

        await RunAsync();
        await RunAsync();
        _sent.Should().ContainSingle().Which.Should().Contain("Memoria");

        SdkResponds(true, records: 10, capacity: 100);
        await RunAsync();
        _alerts.Should().OnlyContain(a => a.IsResolved);

        SdkResponds(true, records: 92, capacity: 100);
        await RunAsync();
        _sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task TheHardwareInfo_KeepsPushSdkAndMac()
    {
        var device = Sdk();
        Devices(device);
        SdkResponds(true, records: 10, capacity: 100);

        await RunAsync();

        device.Status.Should().Be(DeviceStatus.Online);
        device.HardwareInfo!.AttendanceRecordCount.Should().Be(10);
        device.HardwareInfo.PushVersion.Should().Be("push");
        device.HardwareInfo.SdkVersion.Should().Be("sdk");
        device.HardwareInfo.MacAddress.Should().Be("mac");
        _client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ASdkClockInUseByADownload_IsSkipped_WithoutConnecting()
    {
        var device = Sdk();
        device.MarkAsOnline();
        Devices(device);
        _deviceBusy = true;

        (await RunAsync()).IsSuccess.Should().BeTrue();

        _client.Verify(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        device.Status.Should().Be(DeviceStatus.Online);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ASdkClockThatThrows_IsMarkedOffline_AndAlertedOnce()
    {
        var device = Sdk();
        device.MarkAsOnline();
        Devices(device);
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("sin respuesta"));

        await RunAsync();
        await RunAsync();

        device.Status.Should().Be(DeviceStatus.Offline);
        _sent.Should().ContainSingle();
        _alerts.Single().Message.Should().Contain("sin respuesta");
    }

    [Fact]
    public async Task AFailedEmail_DoesNotFailTheRun_NorRepeatTheAlert()
    {
        Devices(Adms());
        AdmsOnline(false);
        _email.Setup(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), AlertLevel.SystemFailure, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP caído"));

        (await RunAsync()).IsSuccess.Should().BeTrue();
        await RunAsync();

        _email.Verify(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), AlertLevel.SystemFailure, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Should().ContainSingle();
    }

    [Fact]
    public async Task WithoutRecipients_TheAlertIsRecorded_ButNothingIsSent()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetSystemConfigurationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SystemConfigurationDto>.Success(new SystemConfigurationDto(
                "Empresa", null, TimeSpan.Zero, TimeSpan.FromHours(8), false, false, null, false, AreAlertsEnabled: true)));
        Devices(Adms());
        AdmsOnline(false);

        await RunAsync();

        _sent.Should().BeEmpty();
        _alerts.Should().ContainSingle();
    }
}
