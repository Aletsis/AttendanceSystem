using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class DeviceTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldInstantiateDeviceAndRaiseEvent()
    {
        // Arrange & Act
        var device = Device.Create(
            deviceId: "DEV-001",
            name: "Lector Principal",
            ipAddress: "192.168.1.200",
            port: 4370,
            brand: DeviceBrand.ZKTeco,
            location: "Entrada Principal",
            shouldClearAfterDownload: false,
            downloadMethod: DeviceDownloadMethod.Sdk,
            serialNumber: "SN12345678");

        // Assert
        device.Should().NotBeNull();
        device.Id.Value.Should().Be("DEV-001");
        device.Name.Should().Be("Lector Principal");
        device.IpAddress.Should().Be("192.168.1.200");
        device.Port.Should().Be(4370);
        device.Brand.Should().Be(DeviceBrand.ZKTeco);
        device.IsActive.Should().BeTrue();
        device.Status.Should().Be(DeviceStatus.Disconnected);
        device.HardwareInfo.SerialNumber.Should().Be("SN12345678");

        device.DomainEvents.Should().ContainSingle(e => e is DeviceRegisteredEvent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsEmpty_ShouldThrowDomainException(string invalidName)
    {
        // Act
        var act = () => Device.Create("DEV-001", invalidName, "192.168.1.200", 4370);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*nombre del dispositivo es requerido*");
    }

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("invalid-ip")]
    [InlineData("")]
    public void Create_WhenIpAddressIsInvalid_ShouldThrowDomainException(string invalidIp)
    {
        // Act
        var act = () => Device.Create("DEV-001", "Lector 1", invalidIp, 4370);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Create_WhenPortIsOutOfRange_ShouldThrowDomainException(int invalidPort)
    {
        // Act
        var act = () => Device.Create("DEV-001", "Lector 1", "192.168.1.200", invalidPort);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*Puerto inválido*");
    }

    [Fact]
    public void Deactivate_WhenActive_ShouldSetInactiveAndRaiseEvent()
    {
        // Arrange
        var device = Device.Create("DEV-001", "Lector 1", "192.168.1.200", 4370);
        device.ClearDomainEvents();

        // Act
        device.Deactivate();

        // Assert
        device.IsActive.Should().BeFalse();
        device.Status.Should().Be(DeviceStatus.Disconnected);
        device.DomainEvents.Should().ContainSingle(e => e is DeviceDeactivatedEvent);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_ShouldThrowDomainException()
    {
        // Arrange
        var device = Device.Create("DEV-001", "Lector 1", "192.168.1.200", 4370);
        device.Deactivate();

        // Act
        var act = () => device.Deactivate();

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*ya está inactivo*");
    }

    [Fact]
    public void Activate_WhenInactive_ShouldSetActiveAndRaiseEvent()
    {
        // Arrange
        var device = Device.Create("DEV-001", "Lector 1", "192.168.1.200", 4370);
        device.Deactivate();
        device.ClearDomainEvents();

        // Act
        device.Activate();

        // Assert
        device.IsActive.Should().BeTrue();
        device.DomainEvents.Should().ContainSingle(e => e is DeviceActivatedEvent);
    }

    [Fact]
    public void RecordSuccessfulDownload_ShouldIncrementCountAndUpdateStatus()
    {
        // Arrange
        var device = Device.Create("DEV-001", "Lector 1", "192.168.1.200", 4370);
        device.ClearDomainEvents();
        var downloadTime = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);

        // Act
        device.RecordSuccessfulDownload(recordCount: 25, downloadTimestamp: downloadTime);

        // Assert
        device.TotalDownloadCount.Should().Be(1);
        device.LastDownloadAt.Should().Be(downloadTime);
        device.Status.Should().Be(DeviceStatus.Online);
        device.DomainEvents.Should().ContainSingle(e => e is DeviceDownloadCompletedEvent);
    }

    [Fact]
    public void RecordFailedDownload_ShouldSetStatusToErrorAndRaiseEvent()
    {
        // Arrange
        var device = Device.Create("DEV-001", "Lector 1", "192.168.1.200", 4370);
        device.ClearDomainEvents();

        // Act
        device.RecordFailedDownload("Timeout de conexión");

        // Assert
        device.Status.Should().Be(DeviceStatus.Error);
        device.DomainEvents.Should().ContainSingle(e => e is DeviceDownloadFailedEvent);
    }
}
