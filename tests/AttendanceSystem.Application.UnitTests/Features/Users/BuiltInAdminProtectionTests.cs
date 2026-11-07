using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.Features.Users.Commands.DeleteUser;
using AttendanceSystem.Application.Features.Users.Commands.UpdateUser;
using AttendanceSystem.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Users;

/// <summary>El administrador del sistema no se puede eliminar, desactivar ni quedar sin rol de administrador.</summary>
public class BuiltInAdminProtectionTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly ApplicationUser _admin = new() { Id = "admin-id", UserName = "admin", Email = "admin@attendance.com", IsActive = true };

    public BuiltInAdminProtectionTests()
    {
        _userManager = new Mock<UserManager<ApplicationUser>>(
            new Mock<IUserStore<ApplicationUser>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(u => u.FindByIdAsync("admin-id")).ReturnsAsync(_admin);
        _userManager.Setup(u => u.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<string> { AppRoles.Administrator });
    }

    [Fact]
    public async Task Built_in_admin_cannot_be_deleted()
    {
        var act = () => new DeleteUserCommandHandler(_userManager.Object).Handle(new DeleteUserCommand("admin-id"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no se puede eliminar*");
        _userManager.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Built_in_admin_cannot_be_deactivated()
    {
        var command = new UpdateUserCommand("admin-id", "admin@attendance.com", "Admin", IsActive: false, new List<string> { AppRoles.Administrator });

        var act = () => new UpdateUserCommandHandler(_userManager.Object).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no se puede desactivar*");
        _admin.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Built_in_admin_keeps_the_administrator_role()
    {
        var command = new UpdateUserCommand("admin-id", "admin@attendance.com", "Admin", IsActive: true, new List<string> { AppRoles.User });

        var act = () => new UpdateUserCommandHandler(_userManager.Object).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*debe conservar el rol*");
        _userManager.Verify(u => u.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task Built_in_admin_can_still_change_name_and_password()
    {
        _userManager.Setup(u => u.GeneratePasswordResetTokenAsync(_admin)).ReturnsAsync("token");
        _userManager.Setup(u => u.ResetPasswordAsync(_admin, "token", "Nueva123!")).ReturnsAsync(IdentityResult.Success);
        var command = new UpdateUserCommand("admin-id", "admin@attendance.com", "Nuevo nombre", IsActive: true, new List<string> { AppRoles.Administrator }, "Nueva123!");

        await new UpdateUserCommandHandler(_userManager.Object).Handle(command, CancellationToken.None);

        _admin.FullName.Should().Be("Nuevo nombre");
        _userManager.Verify(u => u.ResetPasswordAsync(_admin, "token", "Nueva123!"), Times.Once);
    }
}
