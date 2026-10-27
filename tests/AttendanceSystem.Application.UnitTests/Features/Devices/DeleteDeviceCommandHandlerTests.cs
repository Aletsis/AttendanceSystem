using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.Features.Devices.Commands.DeleteDevice;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

public class DeleteDeviceCommandHandlerTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Device _device = Device.Create(Id.ToString(), "Reloj Acceso", "192.168.1.50", 4370);
    private readonly DeleteDeviceCommandHandler _handler;

    public DeleteDeviceCommandHandlerTests()
    {
        _devices.Setup(r => r.GetByIdAsync(DeviceId.From(Id.ToString()), It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        _handler = new DeleteDeviceCommandHandler(_devices.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_WithoutHistory_DeletesTheDevice()
    {
        _devices.Setup(r => r.HasHistoryAsync(It.IsAny<DeviceId>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _handler.Handle(new DeleteDeviceCommand(Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DeleteDeviceOutcome.Deleted);
        _devices.Verify(r => r.Delete(_device), Times.Once);
    }

    [Fact]
    public async Task Handle_WithHistory_OnlyDeactivatesSoPunchesStayLinked()
    {
        _devices.Setup(r => r.HasHistoryAsync(It.IsAny<DeviceId>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(new DeleteDeviceCommand(Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DeleteDeviceOutcome.Deactivated);
        _device.IsActive.Should().BeFalse();
        _devices.Verify(r => r.Delete(It.IsAny<Device>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithHistoryAndAlreadyInactive_StaysInactive()
    {
        _device.Deactivate();
        _devices.Setup(r => r.HasHistoryAsync(It.IsAny<DeviceId>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(new DeleteDeviceCommand(Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DeleteDeviceOutcome.Deactivated);
    }

    [Fact]
    public void ClearingTheClockAfterDownloadRequiresDeviceManagement()
    {
        new DownloadFromDeviceCommand("x").RequiredPermissions().Should().BeEmpty();
        new DownloadFromDeviceCommand("x", ClearDeviceAfterDownload: true).RequiredPermissions()
            .Should().BeEquivalentTo(new[] { Permissions.DevicesManage });
        new DownloadFromDeviceCommand("x", ForceFullSync: true, ClearDeviceAfterDownload: true).RequiredPermissions()
            .Should().BeEquivalentTo(new[] { Permissions.DevicesForceFullDownload, Permissions.DevicesManage });
    }
}
