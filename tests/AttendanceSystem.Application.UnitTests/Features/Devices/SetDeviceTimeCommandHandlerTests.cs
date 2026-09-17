using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

public class SetDeviceTimeCommandHandlerTests
{
    private readonly Mock<IDeviceClientFactory> _deviceClientFactoryMock;
    private readonly Mock<IDeviceQueries> _deviceQueriesMock;
    private readonly Mock<IDeviceClient> _deviceClientMock;
    private readonly SetDeviceTimeHandler _handler;

    private readonly DeviceDto _sampleDevice;

    public SetDeviceTimeCommandHandlerTests()
    {
        _deviceClientFactoryMock = new Mock<IDeviceClientFactory>();
        _deviceQueriesMock = new Mock<IDeviceQueries>();
        _deviceClientMock = new Mock<IDeviceClient>();

        _deviceClientFactoryMock
            .Setup(f => f.GetClient(It.IsAny<DeviceDto>()))
            .Returns(_deviceClientMock.Object);

        _handler = new SetDeviceTimeHandler(
            _deviceClientFactoryMock.Object,
            _deviceQueriesMock.Object);

        _sampleDevice = new DeviceDto(
            DeviceId: "DEV-100",
            Name: "Lector Principal",
            IpAddress: "192.168.1.50",
            Port: 4370,
            Location: "Entrada",
            IsActive: true,
            Status: "Online",
            Brand: DeviceBrand.ZKTeco,
            DownloadMethod: DeviceDownloadMethod.Sdk,
            LastDownloadAt: null,
            TotalDownloadCount: 0);
    }

    [Fact]
    public async Task Handle_WhenDeviceNotFound_ShouldReturnFailure()
    {
        // Arrange
        var command = new SetDeviceTimeCommand("UNKNOWN-DEV", DateTime.UtcNow);

        _deviceQueriesMock
            .Setup(q => q.GetDeviceByIdAsync("UNKNOWN-DEV", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceDto?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Device.NotFound");
    }

    [Fact]
    public async Task Handle_WhenConnectionFails_ShouldReturnFailure()
    {
        // Arrange
        var command = new SetDeviceTimeCommand("DEV-100", DateTime.UtcNow);

        _deviceQueriesMock
            .Setup(q => q.GetDeviceByIdAsync("DEV-100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleDevice);

        _deviceClientMock
            .Setup(c => c.ConnectAsync(_sampleDevice.IpAddress, _sampleDevice.Port, _sampleDevice.Username, _sampleDevice.Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Device.ConnectionFailed");
    }

    [Fact]
    public async Task Handle_WhenSuccessful_ShouldSetTimeAndDisconnect()
    {
        // Arrange
        var targetTime = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var command = new SetDeviceTimeCommand("DEV-100", targetTime);

        _deviceQueriesMock
            .Setup(q => q.GetDeviceByIdAsync("DEV-100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleDevice);

        _deviceClientMock
            .Setup(c => c.ConnectAsync(_sampleDevice.IpAddress, _sampleDevice.Port, _sampleDevice.Username, _sampleDevice.Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _deviceClientMock
            .Setup(c => c.SetDeviceTimeAsync(targetTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _deviceClientMock.Verify(c => c.SetDeviceTimeAsync(targetTime, It.IsAny<CancellationToken>()), Times.Once);
        _deviceClientMock.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
