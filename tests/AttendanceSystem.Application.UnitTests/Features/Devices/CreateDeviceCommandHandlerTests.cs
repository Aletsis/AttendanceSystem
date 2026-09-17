using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Devices.Commands.CreateDevice;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

public class CreateDeviceCommandHandlerTests
{
    private readonly Mock<IDeviceRepository> _deviceRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IDeviceClientFactory> _deviceClientFactoryMock;
    private readonly Mock<IDeviceClient> _deviceClientMock;
    private readonly Mock<ILogger<CreateDeviceCommandHandler>> _loggerMock;
    private readonly CreateDeviceCommandHandler _handler;

    public CreateDeviceCommandHandlerTests()
    {
        _deviceRepositoryMock = new Mock<IDeviceRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _deviceClientFactoryMock = new Mock<IDeviceClientFactory>();
        _deviceClientMock = new Mock<IDeviceClient>();
        _loggerMock = new Mock<ILogger<CreateDeviceCommandHandler>>();

        _deviceClientFactoryMock
            .Setup(f => f.GetClient(It.IsAny<DeviceBrand>()))
            .Returns(_deviceClientMock.Object);

        _handler = new CreateDeviceCommandHandler(
            _deviceRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _deviceClientFactoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenSdkDeviceConnectsSuccessfully_ShouldPopulateHardwareInfoAndSave()
    {
        // Arrange
        var command = new CreateDeviceCommand(
            Name: "Reloj Checador Acceso",
            IpAddress: "192.168.1.200",
            Port: 4370,
            Location: "Acceso Principal",
            Brand: DeviceBrand.ZKTeco,
            ShouldClearAfterDownload: false,
            DownloadMethod: DeviceDownloadMethod.Sdk);

        _deviceClientMock
            .Setup(c => c.ConnectAsync(command.IpAddress, command.Port, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var deviceInfo = new DeviceInfoDto(
            SerialNumber: "ZK-987654321",
            DeviceName: "uFace800",
            FirmwareVersion: "Ver 8.0.2",
            Platform: "ZMM220",
            UserCount: 15,
            FingerprintCount: 30,
            FaceCount: 10,
            AttendanceRecordCount: 500,
            UserCapacity: 3000,
            FingerprintCapacity: 4000,
            FaceCapacity: 1200,
            AttendanceRecordCapacity: 100000);

        _deviceClientMock
            .Setup(c => c.GetDeviceInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(deviceInfo);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        _deviceRepositoryMock.Verify(r => r.AddAsync(It.Is<Device>(d =>
            d.Name == "Reloj Checador Acceso" &&
            d.IpAddress == "192.168.1.200" &&
            d.HardwareInfo.SerialNumber == "ZK-987654321" &&
            d.HardwareInfo.FirmwareVersion == "Ver 8.0.2"), It.IsAny<CancellationToken>()), Times.Once);

        _deviceClientMock.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenConnectionFails_ShouldStillSaveDeviceGracefully()
    {
        // Arrange
        var command = new CreateDeviceCommand(
            Name: "Reloj Checador Comedor",
            IpAddress: "192.168.1.201",
            Port: 4370,
            Location: "Comedor",
            Brand: DeviceBrand.ZKTeco,
            ShouldClearAfterDownload: false,
            DownloadMethod: DeviceDownloadMethod.Sdk);

        _deviceClientMock
            .Setup(c => c.ConnectAsync(command.IpAddress, command.Port, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _deviceRepositoryMock.Verify(r => r.AddAsync(It.Is<Device>(d =>
            d.Name == "Reloj Checador Comedor" &&
            d.IpAddress == "192.168.1.201"), It.IsAny<CancellationToken>()), Times.Once);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
