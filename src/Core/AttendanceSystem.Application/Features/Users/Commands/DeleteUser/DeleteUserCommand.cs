using MediatR;
using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Features.Users.Commands.DeleteUser;

[RequiresPermission(Permissions.UsersManage)]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Unit>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public DeleteUserCommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Unit> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            throw new Exception($"Usuario con ID {request.UserId} no encontrado.");
        }

        if (BuiltInAdmin.Is(user.UserName))
            throw new InvalidOperationException($"El usuario '{user.UserName}' es el administrador del sistema y no se puede eliminar.");

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new Exception($"Error al eliminar usuario: {errors}");
        }

        return Unit.Value;
    }
}
