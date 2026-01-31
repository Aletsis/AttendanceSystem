using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de autenticación usando ASP.NET Core Identity
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, ILogger<AuthenticationService> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        try
        {
            var result = await _signInManager.PasswordSignInAsync(username, password, isPersistent: true, lockoutOnFailure: false);
            
            if (result.Succeeded)
            {
                _logger.LogInformation("Usuario {Username} autenticado existosamente", username);
                return true;
            }
            
            _logger.LogWarning("Fallo en autenticación para usuario {Username}", username);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la autenticación de {Username}", username);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsAdministratorAsync(string username)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user == null) return false;
        
        // Por ahora retornamos true si el usuario existe. 
        // TODO: Implementar roles reales
        return true;
        // return await _userManager.IsInRoleAsync(user, "Admin");
    }

    /// <inheritdoc/>
    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("Usuario cerró sesión");
    }
}