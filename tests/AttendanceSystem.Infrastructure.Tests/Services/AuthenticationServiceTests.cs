using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class AuthenticationServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ILogger<AuthenticationService>> _loggerMock;
    private readonly AuthenticationService _service;

    public AuthenticationServiceTests()
    {
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null!, null!, null!, null!);

        _loggerMock = new Mock<ILogger<AuthenticationService>>();

        _service = new AuthenticationService(
            _signInManagerMock.Object,
            _userManagerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenCredentialsValid_ShouldReturnTrue()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "admin", IsActive = true };
        _userManagerMock
            .Setup(u => u.FindByNameAsync("admin"))
            .ReturnsAsync(user);

        _signInManagerMock
            .Setup(s => s.PasswordSignInAsync("admin", "Admin123!", true, true))
            .ReturnsAsync(SignInResult.Success);

        // Act
        var result = await _service.AuthenticateAsync("admin", "Admin123!");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenCredentialsInvalid_ShouldReturnFalse()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "admin", IsActive = true };
        _userManagerMock
            .Setup(u => u.FindByNameAsync("admin"))
            .ReturnsAsync(user);

        _signInManagerMock
            .Setup(s => s.PasswordSignInAsync("admin", "WrongPass", true, true))
            .ReturnsAsync(SignInResult.Failed);

        // Act
        var result = await _service.AuthenticateAsync("admin", "WrongPass");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenUserInactive_ShouldReturnFalseWithoutAttemptingSignIn()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "inactive_user", IsActive = false };
        _userManagerMock
            .Setup(u => u.FindByNameAsync("inactive_user"))
            .ReturnsAsync(user);

        // Act
        var result = await _service.AuthenticateAsync("inactive_user", "AnyPassword123!");

        // Assert
        result.Should().BeFalse();
        _signInManagerMock.Verify(
            s => s.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenUserLockedOut_ShouldReturnFalse()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "locked_user", IsActive = true };
        _userManagerMock
            .Setup(u => u.FindByNameAsync("locked_user"))
            .ReturnsAsync(user);

        _signInManagerMock
            .Setup(s => s.PasswordSignInAsync("locked_user", "Pass123!", true, true))
            .ReturnsAsync(SignInResult.LockedOut);

        // Act
        var result = await _service.AuthenticateAsync("locked_user", "Pass123!");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAdministratorAsync_WhenUserInAdminRole_ShouldReturnTrue()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "admin_user", IsActive = true };
        _userManagerMock
            .Setup(u => u.FindByNameAsync("admin_user"))
            .ReturnsAsync(user);
        _userManagerMock
            .Setup(u => u.IsInRoleAsync(user, "Administrador"))
            .ReturnsAsync(true);

        // Act
        var result = await _service.IsAdministratorAsync("admin_user");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAdministratorAsync_WhenUserInactive_ShouldReturnFalse()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "inactive_admin", IsActive = false };
        _userManagerMock
            .Setup(u => u.FindByNameAsync("inactive_admin"))
            .ReturnsAsync(user);

        // Act
        var result = await _service.IsAdministratorAsync("inactive_admin");

        // Assert
        result.Should().BeFalse();
        _userManagerMock.Verify(u => u.IsInRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IsAdministratorAsync_WhenUserNotFound_ShouldReturnFalse()
    {
        // Arrange
        _userManagerMock
            .Setup(u => u.FindByNameAsync("unknown_user"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _service.IsAdministratorAsync("unknown_user");

        // Assert
        result.Should().BeFalse();
    }
}
