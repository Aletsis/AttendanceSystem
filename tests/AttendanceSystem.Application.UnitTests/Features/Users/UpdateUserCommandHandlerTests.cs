using AttendanceSystem.Application.Features.Users.Commands.UpdateUser;
using AttendanceSystem.Domain.Entities;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Users;

public class UpdateUserCommandHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly UpdateUserCommandHandler _handler;

    public UpdateUserCommandHandlerTests()
    {
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new UpdateUserCommandHandler(_userManagerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ShouldThrowException()
    {
        // Arrange
        var command = new UpdateUserCommand(
            UserId: "non-existent-id",
            Email: "nuevo@empresa.com",
            FullName: "Nuevo Nombre",
            IsActive: true,
            Roles: new List<string>());

        _userManagerMock
            .Setup(u => u.FindByIdAsync("non-existent-id"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*no encontrado*");
    }

    [Fact]
    public async Task Handle_WhenUserExists_ShouldUpdateDetailsAndSyncRoles()
    {
        // Arrange
        var existingUser = new ApplicationUser
        {
            Id = "user-123",
            UserName = "juan",
            Email = "juan@viejo.com",
            FullName = "Juan Perez",
            IsActive = true
        };

        var command = new UpdateUserCommand(
            UserId: "user-123",
            Email: "juan@nuevo.com",
            FullName: "Juan Carlos Perez",
            IsActive: false,
            Roles: new List<string> { "Supervisor", "Admin" });

        _userManagerMock
            .Setup(u => u.FindByIdAsync("user-123"))
            .ReturnsAsync(existingUser);

        _userManagerMock
            .Setup(u => u.UpdateAsync(existingUser))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock
            .Setup(u => u.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { "Operador", "Supervisor" });

        _userManagerMock
            .Setup(u => u.AddToRolesAsync(existingUser, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock
            .Setup(u => u.RemoveFromRolesAsync(existingUser, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(Unit.Value);
        existingUser.Email.Should().Be("juan@nuevo.com");
        existingUser.FullName.Should().Be("Juan Carlos Perez");
        existingUser.IsActive.Should().BeFalse();

        // Roles to add: "Admin"
        _userManagerMock.Verify(u => u.AddToRolesAsync(existingUser, It.Is<IEnumerable<string>>(r => r.Contains("Admin"))), Times.Once);
        // Roles to remove: "Operador"
        _userManagerMock.Verify(u => u.RemoveFromRolesAsync(existingUser, It.Is<IEnumerable<string>>(r => r.Contains("Operador"))), Times.Once);
    }
}
