using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Features.Users.Commands.ChangeOwnPassword;

/// <summary>
/// Caso 13: cualquier usuario cambia su propia contraseña indicando la actual. Identity renueva el sello de seguridad,
/// así que las demás sesiones abiertas del usuario se cierran.
/// </summary>
public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result>;

public sealed class ChangeOwnPasswordCommandHandler : IRequestHandler<ChangeOwnPasswordCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;

    public ChangeOwnPasswordCommandHandler(UserManager<ApplicationUser> userManager, ICurrentUserService currentUser)
    {
        _userManager = userManager;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(ChangeOwnPasswordCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Result.Failure("Inicie sesión para cambiar su contraseña.");
        }
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Result.Failure("Indique la nueva contraseña.");
        }

        var user = await _userManager.FindByIdAsync(_currentUser.UserId);
        if (user == null) return Result.Failure("Usuario no encontrado.");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Result.Failure(result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? "La contraseña actual no es correcta."
                : string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        user.LastModifiedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        return Result.Success();
    }
}
