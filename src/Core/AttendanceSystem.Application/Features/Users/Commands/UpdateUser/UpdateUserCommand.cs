using MediatR;
using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Features.Users.Commands.UpdateUser;

public sealed record UpdateUserCommand(
    string UserId,
    string Email,
    string FullName,
    bool IsActive,
    List<string> Roles,
    string? NewPassword = null) : IRequest<Unit>;

public sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, Unit>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UpdateUserCommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Unit> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            throw new Exception($"Usuario con ID {request.UserId} no encontrado.");
        }

        user.Email = request.Email;
        user.FullName = request.FullName;
        user.IsActive = request.IsActive;
        user.LastModifiedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new Exception($"Error al actualizar usuario: {errors}");
        }

        // Gestión de Roles
        var currentRoles = await _userManager.GetRolesAsync(user);
        var rolesToAdd = request.Roles.Except(currentRoles).ToList();
        var rolesToRemove = currentRoles.Except(request.Roles).ToList();

        if (rolesToAdd.Any())
        {
            await _userManager.AddToRolesAsync(user, rolesToAdd);
        }

        if (rolesToRemove.Any())
        {
            await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
        }

        // Cambio de contraseña si se proporciona
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var pwdResult = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!pwdResult.Succeeded)
            {
                 var errors = string.Join(", ", pwdResult.Errors.Select(e => e.Description));
                 throw new Exception($"Error al cambiar contraseña: {errors}");
            }
        }

        return Unit.Value;
    }
}
