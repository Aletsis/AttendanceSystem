using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class RestoreStateServiceTests
{
    [Fact]
    public void RestoreStateService_ShouldInitiallyBeFalse()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);
        service.IsRestoreInProgress.Should().BeFalse();
    }

    [Fact]
    public void EnterRestoreMode_ShouldSetIsRestoreInProgressToTrue()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);

        service.EnterRestoreMode();
        service.IsRestoreInProgress.Should().BeTrue();
    }

    [Fact]
    public void ExitRestoreMode_ShouldSetIsRestoreInProgressToFalse()
    {
        var service = new RestoreStateService(NullLogger<RestoreStateService>.Instance);

        service.EnterRestoreMode();
        service.IsRestoreInProgress.Should().BeTrue();

        service.ExitRestoreMode();
        service.IsRestoreInProgress.Should().BeFalse();
    }
}
