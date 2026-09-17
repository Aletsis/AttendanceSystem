using AttendanceSystem.Application.Features.Users.Commands.CreateUser;
using AttendanceSystem.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Users;

public class CreateUserCommandHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly CreateUserCommandHandler _handler;

    public CreateUserCommandHandlerTests()
    {
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new CreateUserCommandHandler(_userManagerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyExists_ShouldThrowException()
    {
        // Arrange
        var command = new CreateUserCommand(
            UserName: "admin",
            Email: "admin@empresa.com",
            FullName: "Administrador General",
            Password: "Password123!",
            IsActive: true,
            Roles: new List<string> { "Admin" });

        _userManagerMock
            .Setup(u => u.FindByNameAsync("admin"))
            .ReturnsAsync(new ApplicationUser { UserName = "admin" });

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*ya existe*");
    }

    [Fact]
    public async Task Handle_WhenValidParameters_ShouldCreateUserAndAssignRoles()
    {
        // Arrange
        var command = new CreateUserCommand(
            UserName: "operador1",
            Email: "operador@empresa.com",
            FullName: "Operador Sistema",
            Password: "Password123!",
            IsActive: true,
            Roles: new List<string> { "Operador", "Supervisor" });

        _userManagerMock
            .Setup(u => u.FindByNameAsync("operador1"))
            .ReturnsAsync((ApplicationUser?)null);

        _userManagerMock
            .Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), "Password123!"))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock
            .Setup(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), command.Roles))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _userManagerMock.Verify(u => u.CreateAsync(It.Is<ApplicationUser>(u =>
            u.UserName == "operador1" &&
            u.Email == "operador@empresa.com" &&
            u.FullName == "Operador Sistema" &&
            u.IsActive == true), "Password123!"), Times.Once);

        _userManagerMock.Verify(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), command.Roles), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCreateFails_ShouldThrowExceptionWithErrors()
    {
        // Arrange
        var command = new CreateUserCommand(
            UserName: "usuario_invalido",
            Email: "email@empresa.com",
            FullName: "Nombre",
            Password: "123",
            IsActive: true,
            Roles: new List<string>());

        _userManagerMock
            .Setup(u => u.FindByNameAsync("usuario_invalido"))
            .ReturnsAsync((ApplicationUser?)null);

        var error = new IdentityError { Description = "Password too weak" };
        _userManagerMock
            .Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), "123"))
            .ReturnsAsync(IdentityResult.Failed(error));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Password too weak*");
    }
}
