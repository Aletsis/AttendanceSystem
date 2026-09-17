using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence.Repositories;

public class DeviceRepositoryTests
{
    private AttendanceDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var publisherMock = new Mock<IPublisher>();
        return new AttendanceDbContext(options, publisherMock.Object);
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_ShouldPersistAndRetrieveDevice()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new DeviceRepository(context);

        var device = Device.Create(
            deviceId: "DEV-101",
            name: "Lector Torniquete 1",
            ipAddress: "192.168.1.150",
            port: 4370,
            brand: DeviceBrand.ZKTeco,
            location: "Torniquetes",
            shouldClearAfterDownload: false,
            downloadMethod: DeviceDownloadMethod.Sdk,
            serialNumber: "SN-998877");

        // Act
        await repository.AddAsync(device);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(device.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(device.Id);
        retrieved.Name.Should().Be("Lector Torniquete 1");
        retrieved.IpAddress.Should().Be("192.168.1.150");
        retrieved.HardwareInfo.SerialNumber.Should().Be("SN-998877");
    }

    [Fact]
    public async Task GetActiveDevicesAsync_ShouldOnlyReturnActiveDevices()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new DeviceRepository(context);

        var devActive = Device.Create("DEV-ACT", "Lector Activo", "192.168.1.151", 4370);
        var devInactive = Device.Create("DEV-INACT", "Lector Inactivo", "192.168.1.152", 4370);
        devInactive.Deactivate();

        await repository.AddAsync(devActive);
        await repository.AddAsync(devInactive);
        await context.SaveChangesAsync();

        // Act
        var activeDevices = await repository.GetActiveDevicesAsync();

        // Assert
        activeDevices.Should().HaveCount(1);
        activeDevices.First().Id.Should().Be(devActive.Id);
    }

    [Fact]
    public async Task GetBySerialNumberAsync_ShouldFindDeviceByHardwareSerialNumber()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new DeviceRepository(context);

        var device = Device.Create("DEV-SN", "Lector Bio", "192.168.1.155", 4370, serialNumber: "ZK-UNIQUE-SN-001");
        await repository.AddAsync(device);
        await context.SaveChangesAsync();

        // Act
        var found = await repository.GetBySerialNumberAsync("ZK-UNIQUE-SN-001");

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(device.Id);
    }
}
