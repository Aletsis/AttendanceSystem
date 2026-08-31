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
        _signInManagerMock
            .Setup(s => s.PasswordSignInAsync("admin", "Admin123!", true, false))
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
        _signInManagerMock
            .Setup(s => s.PasswordSignInAsync("admin", "WrongPass", true, false))
            .ReturnsAsync(SignInResult.Failed);

        // Act
        var result = await _service.AuthenticateAsync("admin", "WrongPass");

        // Assert
        result.Should().BeFalse();
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
