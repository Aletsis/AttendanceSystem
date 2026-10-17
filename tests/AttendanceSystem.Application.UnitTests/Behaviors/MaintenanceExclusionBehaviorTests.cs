using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Behaviors;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Exceptions;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Behaviors;

public class MaintenanceExclusionBehaviorTests
{
    public record SampleCommand() : IRequest<string>;
    public record SampleQuery() : IRequest<string>;
    public record RestoreBackupCommand() : IRequest<string>;

    [Fact]
    public async Task Handle_Command_WhenCanWriteIsTrue_ShouldExecuteAndRegisterWriter()
    {
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.SetupGet(s => s.CanWrite).Returns(true);
        var mockDisposable = new Mock<IDisposable>();
        mockRestoreState.Setup(s => s.RegisterActiveWriter()).Returns(mockDisposable.Object);

        var behavior = new MaintenanceExclusionBehavior<SampleCommand, string>(
            mockRestoreState.Object,
            NullLogger<MaintenanceExclusionBehavior<SampleCommand, string>>.Instance);

        var executed = false;
        var result = await behavior.Handle(new SampleCommand(), (ct) =>
        {
            executed = true;
            return Task.FromResult("OK");
        }, CancellationToken.None);

        result.Should().Be("OK");
        executed.Should().BeTrue();
        mockRestoreState.Verify(s => s.RegisterActiveWriter(), Times.Once);
        mockDisposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Handle_Command_WhenCanWriteIsFalse_ShouldThrowDatabaseMaintenanceException_H05()
    {
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.SetupGet(s => s.CanWrite).Returns(false);
        mockRestoreState.SetupGet(s => s.CurrentState).Returns(MaintenanceState.Restoring);
        mockRestoreState.SetupGet(s => s.CurrentOwner).Returns("Admin_Process");

        var behavior = new MaintenanceExclusionBehavior<SampleCommand, string>(
            mockRestoreState.Object,
            NullLogger<MaintenanceExclusionBehavior<SampleCommand, string>>.Instance);

        var executed = false;
        var act = () => behavior.Handle(new SampleCommand(), (ct) =>
        {
            executed = true;
            return Task.FromResult("OK");
        }, CancellationToken.None);

        await act.Should().ThrowAsync<DatabaseMaintenanceException>()
            .Where(ex => ex.State == MaintenanceState.Restoring && ex.Owner == "Admin_Process");

        executed.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RestoreBackupCommand_WhenInMaintenance_ShouldBeExemptFromExclusion()
    {
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.SetupGet(s => s.CanWrite).Returns(false);
        mockRestoreState.SetupGet(s => s.CurrentState).Returns(MaintenanceState.Restoring);

        var behavior = new MaintenanceExclusionBehavior<RestoreBackupCommand, string>(
            mockRestoreState.Object,
            NullLogger<MaintenanceExclusionBehavior<RestoreBackupCommand, string>>.Instance);

        var executed = false;
        var result = await behavior.Handle(new RestoreBackupCommand(), (ct) =>
        {
            executed = true;
            return Task.FromResult("OK");
        }, CancellationToken.None);

        result.Should().Be("OK");
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Query_WhenStateIsRestoring_ShouldThrowDatabaseMaintenanceException()
    {
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.SetupGet(s => s.CurrentState).Returns(MaintenanceState.Restoring);

        var behavior = new MaintenanceExclusionBehavior<SampleQuery, string>(
            mockRestoreState.Object,
            NullLogger<MaintenanceExclusionBehavior<SampleQuery, string>>.Instance);

        var act = () => behavior.Handle(new SampleQuery(), (ct) => Task.FromResult("Data"), CancellationToken.None);

        await act.Should().ThrowAsync<DatabaseMaintenanceException>();
    }

    [Fact]
    public async Task Handle_Query_WhenStateIsPreparing_ShouldAllowExecution()
    {
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.SetupGet(s => s.CurrentState).Returns(MaintenanceState.Preparing);

        var behavior = new MaintenanceExclusionBehavior<SampleQuery, string>(
            mockRestoreState.Object,
            NullLogger<MaintenanceExclusionBehavior<SampleQuery, string>>.Instance);

        var result = await behavior.Handle(new SampleQuery(), (ct) => Task.FromResult("Data"), CancellationToken.None);

        result.Should().Be("Data");
    }
}
