using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Blazor.Server.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Blazor.UnitTests.Middleware;

public class RestoreMaintenanceMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenRestoreNotInProgress_ShouldCallNext()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RestoreMaintenanceMiddleware(next, NullLogger<RestoreMaintenanceMiddleware>.Instance);
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.Setup(s => s.IsRestoreInProgress).Returns(false);

        var context = new DefaultHttpContext();
        context.Request.Path = "/iclock/getrequest";

        // Act
        await middleware.InvokeAsync(context, mockRestoreState.Object);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_WhenRestoreInProgressAndPathIsIclock_ShouldReturn503AndNotCallNext()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RestoreMaintenanceMiddleware(next, NullLogger<RestoreMaintenanceMiddleware>.Instance);
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.Setup(s => s.IsRestoreInProgress).Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Path = "/iclock/getrequest";

        // Act
        await middleware.InvokeAsync(context, mockRestoreState.Object);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task InvokeAsync_WhenRestoreInProgressAndPathIsNotAdms_ShouldCallNext()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RestoreMaintenanceMiddleware(next, NullLogger<RestoreMaintenanceMiddleware>.Instance);
        var mockRestoreState = new Mock<IRestoreStateService>();
        mockRestoreState.Setup(s => s.IsRestoreInProgress).Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Path = "/dashboard";

        // Act
        await middleware.InvokeAsync(context, mockRestoreState.Object);

        // Assert
        nextCalled.Should().BeTrue();
    }
}
