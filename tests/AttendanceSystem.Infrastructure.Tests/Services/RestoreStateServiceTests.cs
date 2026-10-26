using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class RestoreStateServiceTests
{
    [Fact]
    public void RestoreStateService_ShouldInitiallyBeNormalAndCanWrite()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        service.IsRestoreInProgress.Should().BeFalse();
        service.CurrentState.Should().Be(MaintenanceState.Normal);
        service.CurrentOwner.Should().BeNull();
        service.CanWrite.Should().BeTrue();
        service.ActiveWritersCount.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireRestoreLockAsync_ShouldAcquireLockAndSetPreparingState()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);

        var acquired = await service.TryAcquireRestoreLockAsync("Owner_1", "Test restore");

        acquired.Should().BeTrue();
        service.IsRestoreInProgress.Should().BeTrue();
        service.CurrentState.Should().Be(MaintenanceState.Preparing);
        service.CurrentOwner.Should().Be("Owner_1");
        service.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireRestoreLockAsync_ShouldRejectSecondConcurrentRestore_H05()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);

        var first = await service.TryAcquireRestoreLockAsync("Owner_1", "First restore");
        var second = await service.TryAcquireRestoreLockAsync("Owner_2", "Second restore");

        first.Should().BeTrue();
        second.Should().BeFalse();
        service.CurrentOwner.Should().Be("Owner_1");
    }

    [Fact]
    public void RegisterActiveWriter_ShouldIncrementAndDecrementCountOnDispose()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);

        using (var slot1 = service.RegisterActiveWriter())
        {
            service.ActiveWritersCount.Should().Be(1);

            using (var slot2 = service.RegisterActiveWriter())
            {
                service.ActiveWritersCount.Should().Be(2);
            }

            service.ActiveWritersCount.Should().Be(1);
        }

        service.ActiveWritersCount.Should().Be(0);
    }

    [Fact]
    public async Task RegisterActiveWriter_ShouldThrowDatabaseMaintenanceException_WhenInMaintenance()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        await service.TryAcquireRestoreLockAsync("Owner_1", "Test");

        var act = () => service.RegisterActiveWriter();

        act.Should().Throw<DatabaseMaintenanceException>()
            .Which.State.Should().Be(MaintenanceState.Preparing);
    }

    [Fact]
    public async Task DrainActiveWritersAsync_ShouldWaitUntilAllWritersDispose()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        var slot = service.RegisterActiveWriter();

        var drainTask = Task.Run(async () =>
        {
            // Límite holgado: la prueba verifica que espera al escritor, no el tiempo (bajo carga el retraso de 100 ms crece).
            await service.DrainActiveWritersAsync(TimeSpan.FromSeconds(30));
        });

        await Task.Delay(100);
        drainTask.IsCompleted.Should().BeFalse();

        slot.Dispose();
        await drainTask;

        service.ActiveWritersCount.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseRestoreLockAsync_WithSuccess_ShouldReturnToNormal()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        await service.TryAcquireRestoreLockAsync("Owner_1", "Test");

        await service.ReleaseRestoreLockAsync("Owner_1", success: true);

        service.IsRestoreInProgress.Should().BeFalse();
        service.CurrentState.Should().Be(MaintenanceState.Normal);
        service.CurrentOwner.Should().BeNull();
        service.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseRestoreLockAsync_WithFailure_ShouldKeepMaintenanceInFailedState_H04_H05()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        await service.TryAcquireRestoreLockAsync("Owner_1", "Test");

        await service.ReleaseRestoreLockAsync("Owner_1", success: false);

        service.IsRestoreInProgress.Should().BeTrue();
        service.CurrentState.Should().Be(MaintenanceState.Failed);
        service.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireRestoreLockAsync_FromFailed_ShouldBeAllowed_SoARestoreCanRecover()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        await service.TryAcquireRestoreLockAsync("Owner_1", "Test");
        await service.ReleaseRestoreLockAsync("Owner_1", success: false);

        var acquired = await service.TryAcquireRestoreLockAsync("Owner_2", "Recuperación");

        acquired.Should().BeTrue();
        service.CurrentState.Should().Be(MaintenanceState.Preparing);
        service.CurrentOwner.Should().Be("Owner_2");
    }

    [Fact]
    public async Task TransitionStateAsync_ShouldUpdateStateCorrectly()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        await service.TryAcquireRestoreLockAsync("Owner_1", "Test");

        await service.TransitionStateAsync(MaintenanceState.Draining, "Draining");
        service.CurrentState.Should().Be(MaintenanceState.Draining);

        await service.TransitionStateAsync(MaintenanceState.Restoring, "Restoring");
        service.CurrentState.Should().Be(MaintenanceState.Restoring);

        await service.TransitionStateAsync(MaintenanceState.Validating, "Validating");
        service.CurrentState.Should().Be(MaintenanceState.Validating);
    }
}
