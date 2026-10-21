using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Clock;
using AttendanceSystem.Application.Features.Devices.Commands.CreateDevice;
using AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime;
using AttendanceSystem.Application.Features.Devices.Commands.UpdateDevice;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

/// <summary>
/// Reloj en Quintana Roo (UTC-5) y sistema en el centro de México (UTC-6): la hora del reloj se fija, verifica y
/// corrige en SU zona, nunca en la del servidor.
/// </summary>
public class DeviceClockTests
{
    private const string ClockZone = "America/Cancun";
    private static readonly DateTime UtcNow = new(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc); // 10:00 en UTC-5, 09:00 en UTC-6
    private static readonly DateTime ClockNow = new(2026, 10, 21, 10, 0, 0);

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private static Mock<IAuthoritativeTimeZoneResolver> Resolver(string zone = ClockZone)
    {
        var resolver = new Mock<IAuthoritativeTimeZoneResolver>();
        resolver.Setup(r => r.ResolveForDeviceAsync(It.IsAny<DeviceId?>(), It.IsAny<CancellationToken>())).ReturnsAsync(zone);
        return resolver;
    }

    private static DeviceClockService Clock(string zone = ClockZone) =>
        new(DefaultTimeNormalizer.Instance, Resolver(zone).Object, NullLogger<DeviceClockService>.Instance, new FixedTime(UtcNow));

    [Fact]
    public void ExpectedTime_IsTheCivilTimeOfTheClockZone()
    {
        var clock = Clock();

        clock.ExpectedCivilNow(ClockZone).Should().Be(ClockNow);
        clock.UtcOffsetOf(ClockZone).Should().Be(TimeSpan.FromHours(-5));
        clock.ExpectedCivilNow("America/Mexico_City").Should().Be(ClockNow.AddHours(-1));
    }

    [Fact]
    public async Task Verify_WithinTolerance_DoesNotTouchTheClock()
    {
        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.GetDeviceTimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ClockNow.AddSeconds(90));

        var check = await Clock().VerifyAsync(client.Object, ClockZone, "Reloj QRoo");

        check.Corrected.Should().BeFalse();
        check.Drift.Should().Be(TimeSpan.FromSeconds(90));
        client.Verify(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_ClockShowingTheServerZone_IsResynchronizedToItsOwnZone()
    {
        // Escenario 4: el reloj quedó con la hora del servidor (09:00) cuando en su zona son las 10:00.
        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.GetDeviceTimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ClockNow.AddHours(-1));
        client.Setup(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var check = await Clock().VerifyAsync(client.Object, ClockZone, "Reloj QRoo");

        check.Corrected.Should().BeTrue();
        check.Drift.Should().Be(TimeSpan.FromHours(-1));
        client.Verify(c => c.SetDeviceClockAsync(ClockNow, TimeSpan.FromHours(-5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_WhenTheProtocolCannotReadTheTime_DoesNothing()
    {
        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.GetDeviceTimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DateTime?)null);

        var check = await Clock().VerifyAsync(client.Object, ClockZone, "Reloj ADMS");

        check.Verified.Should().BeFalse();
        client.Verify(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(DeviceDownloadMethod.Sdk)]
    [InlineData(DeviceDownloadMethod.Adms)]
    public async Task Sync_SetsTheCurrentTimeOfTheClockZone(DeviceDownloadMethod method)
    {
        // Escenarios 1 y 2: reloj nuevo en otra zona, por SDK y por ADMS.
        var device = new DeviceDto("D1", "Reloj QRoo", "10.0.0.9", 4370, null, true, "Online", DeviceBrand.ZKTeco, method, null, 0, TimeZoneId: ClockZone);
        var queries = new Mock<IDeviceQueries>();
        queries.Setup(q => q.GetDeviceByIdAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        client.Setup(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var factory = new Mock<IDeviceClientFactory>();
        factory.Setup(f => f.GetClient(device)).Returns(client.Object);

        var result = await new SetDeviceTimeHandler(factory.Object, queries.Object, Clock()).Handle(new SetDeviceTimeCommand("D1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        client.Verify(c => c.SetDeviceClockAsync(ClockNow, TimeSpan.FromHours(-5), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangingTheZone_SavesItAndSynchronizesTheClock()
    {
        // Escenario 3: el reloj se mueve a una sucursal de otra zona.
        var device = Device.Create(Guid.NewGuid().ToString(), "Reloj móvil", "10.0.0.7", 4370, timeZoneId: "America/Mexico_City");
        var repository = new Mock<IDeviceRepository>();
        repository.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<SetDeviceTimeCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var handler = new UpdateDeviceCommandHandler(repository.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ILogger<UpdateDeviceCommandHandler>>(), sender.Object);

        var result = await handler.Handle(Update(device, ClockZone), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        device.TimeZoneId.Should().Be(ClockZone);
        sender.Verify(s => s.Send(It.Is<SetDeviceTimeCommand>(c => c.DeviceId == device.Id.Value && c.CivilTime == null), It.IsAny<CancellationToken>()), Times.Once);

        // Guardar de nuevo sin cambiar la zona no vuelve a tocar el reloj.
        await handler.Handle(Update(device, ClockZone), CancellationToken.None);
        sender.Verify(s => s.Send(It.IsAny<SetDeviceTimeCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownZone_IsRejected()
    {
        var device = Device.Create(Guid.NewGuid().ToString(), "Reloj", "10.0.0.7", 4370);
        var repository = new Mock<IDeviceRepository>();
        repository.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var update = new UpdateDeviceCommandHandler(repository.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ILogger<UpdateDeviceCommandHandler>>());
        var create = new CreateDeviceCommandHandler(repository.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IDeviceClientFactory>(), Mock.Of<ILogger<CreateDeviceCommandHandler>>());

        (await update.Handle(Update(device, "Marte/Olympus"), CancellationToken.None)).IsFailure.Should().BeTrue();
        (await create.Handle(new CreateDeviceCommand("Reloj", "10.0.0.8", 4370, null, DeviceBrand.ZKTeco, false, DeviceDownloadMethod.Adms, "SN1", TimeZoneId: "Marte/Olympus"), CancellationToken.None))
            .IsFailure.Should().BeTrue();
        device.TimeZoneId.Should().BeNull();
    }

    [Fact]
    public void Catalog_ZonesExistOnThisSystem()
    {
        TimeZoneCatalog.Common.Should().OnlyContain(z => TimeZoneCatalog.IsValid(z.Id));
        TimeZoneCatalog.Describe(ClockZone, UtcNow).Should().Be("México – Quintana Roo (UTC−05:00)");
    }

    private static UpdateDeviceCommand Update(Device device, string zone) =>
        new(Guid.Parse(device.Id.Value), device.Name, device.IpAddress, device.Port, device.Location, device.Brand,
            device.ShouldClearAfterDownload, device.DownloadMethod, TimeZoneId: zone);
}
