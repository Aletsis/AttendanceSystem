using System.Security.Claims;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Blazor.Server.Controllers;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Blazor.UnitTests.Controllers;

public class ControllerSecurityTests
{
    [Fact]
    public void ExternalEmployeesController_ShouldRequireAdminRole()
    {
        // Act
        var attrs = typeof(ExternalEmployeesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        // Assert
        attrs.Should().NotBeEmpty();
        attrs.Should().ContainSingle(a => a.Roles == "Administrador");
    }

    [Fact]
    public async Task ExternalLogsController_ReceiveLog_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var senderMock = new Mock<ISender>();
        var logRepoMock = new Mock<IExternalAttendanceLogRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ApiKey", "ValidSuperSecretApiKey"}
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var controller = new ExternalLogsController(
            senderMock.Object,
            logRepoMock.Object,
            branchRepoMock.Object,
            uowMock.Object,
            config,
            NullLogger<ExternalLogsController>.Instance);

        var httpContext = new DefaultHttpContext();
        // No user, no x-api-key header
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ExternalLogRequest { EmployeeId = "101", CheckTime = DateTime.UtcNow };

        // Act
        var result = await controller.ReceiveLog(request);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ExternalLogsController_ReceiveLog_WithValidApiKey_ShouldNotReturnUnauthorized()
    {
        // Arrange
        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(It.IsAny<AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance.RecordAttendanceCommand>(), default))
            .ReturnsAsync(AttendanceSystem.Application.Common.Result<Guid>.Success(Guid.NewGuid()));
        var logRepoMock = new Mock<IExternalAttendanceLogRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ApiKey", "ValidSuperSecretApiKey"}
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var controller = new ExternalLogsController(
            senderMock.Object,
            logRepoMock.Object,
            branchRepoMock.Object,
            uowMock.Object,
            config,
            NullLogger<ExternalLogsController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-api-key"] = "ValidSuperSecretApiKey";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ExternalLogRequest { EmployeeId = "101", CheckTime = DateTime.UtcNow };

        // Act
        var result = await controller.ReceiveLog(request);

        // Assert: It should pass auth and return Ok
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfigurationController_ReloadJobs_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var repoMock = new Mock<ISystemConfigurationRepository>();
        var schedulerMock = new Mock<IAttendanceJobScheduler>();
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ApiKey", "ValidConfigSecret"}
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var controller = new ConfigurationController(
            repoMock.Object,
            schedulerMock.Object,
            config,
            NullLogger<ConfigurationController>.Instance);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await controller.ReloadJobs();

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ConfigurationController_ReloadJobs_WithAdminUser_ShouldPassAuth()
    {
        // Arrange
        var repoMock = new Mock<ISystemConfigurationRepository>();
        var schedulerMock = new Mock<IAttendanceJobScheduler>();
        IConfiguration config = new ConfigurationBuilder().Build();

        var controller = new ConfigurationController(
            repoMock.Object,
            schedulerMock.Object,
            config,
            NullLogger<ConfigurationController>.Instance);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "Administrador")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await controller.ReloadJobs();

        // Assert: passes auth (might be NotFound or Ok based on config, but NOT Unauthorized)
        result.Should().NotBeOfType<UnauthorizedObjectResult>();
    }
}
