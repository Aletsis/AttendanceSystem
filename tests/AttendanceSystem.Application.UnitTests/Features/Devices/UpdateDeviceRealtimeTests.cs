using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Commands.UpdateDevice;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

/// <summary>La opción de tiempo real al editar un reloj.</summary>
public class UpdateDeviceRealtimeTests
{
    private readonly Device _device = Device.Create(Guid.NewGuid().ToString(), "Entrada", "10.0.0.5", 4370);

    [Fact]
    public async Task WithoutTheOption_TheCurrentValueIsKept()
    {
        _device.SetRealtimeEnabled(true);

        (await Handle(realtime: null)).IsSuccess.Should().BeTrue();

        _device.RealtimeEnabled.Should().BeTrue("una pantalla que no muestra la opción no debe apagarla");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithTheOption_ItIsSaved(bool realtime)
    {
        _device.SetRealtimeEnabled(!realtime);

        await Handle(realtime);

        _device.RealtimeEnabled.Should().Be(realtime);
    }

    [Fact]
    public async Task OnAClockThatStopsBeingSdk_ItTurnsOff()
    {
        _device.SetRealtimeEnabled(true);

        (await Handle(realtime: true, DeviceDownloadMethod.Adms)).IsSuccess.Should().BeTrue();

        _device.RealtimeEnabled.Should().BeFalse();
    }

    private Task<AttendanceSystem.Application.Common.Result> Handle(bool? realtime, DeviceDownloadMethod method = DeviceDownloadMethod.Sdk)
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetByIdAsync(It.IsAny<DeviceId>(), It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        var handler = new UpdateDeviceCommandHandler(devices.Object, Mock.Of<IUnitOfWork>(), NullLogger<UpdateDeviceCommandHandler>.Instance);
        return handler.Handle(new UpdateDeviceCommand(Guid.Parse(_device.Id.Value), "Entrada", "10.0.0.5", 4370, null, DeviceBrand.ZKTeco, false, method,
            SerialNumber: method == DeviceDownloadMethod.Adms ? "SN-1" : null, RealtimeEnabled: realtime), CancellationToken.None);
    }
}
