using MediatR;
using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Features.Users.Commands.CreateUser;

public sealed record CreateUserCommand(
    string UserName,
    string Email,
    string FullName,
    string Password,
    bool IsActive,
    List<string> Roles) : IRequest<string>;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, string>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateUserCommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<string> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var existingUser = await _userManager.FindByNameAsync(request.UserName);
        if (existingUser != null)
        {
            throw new Exception($"El nombre de usuario '{request.UserName}' ya existe.");
        }

        var user = new ApplicationUser
        {
            UserName = request.UserName,
            Email = request.Email,
            FullName = request.FullName,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new Exception($"Error al crear usuario: {errors}");
        }

        if (request.Roles != null && request.Roles.Any())
        {
            var roleResult = await _userManager.AddToRolesAsync(user, request.Roles);
            if (!roleResult.Succeeded)
            {
                 var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                 throw new Exception($"Usuario creado, pero falló la asignación de roles: {errors}");
            }
        }

        return user.Id;
    }
}
