using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Application.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace AttendanceSystem.Blazor.Server.Controllers;

[Route("[controller]")]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuthenticationService authService,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] LoginRequest model)
    {
        if (!ModelState.IsValid)
        {
            return Redirect($"/login?error={Uri.EscapeDataString("Por favor complete todos los campos")}");
        }

        try
        {
            // H12: Validar si el usuario existe y está inactivo antes de iniciar sesión
            var user = await _userManager.FindByNameAsync(model.Username)
                       ?? await _userManager.FindByEmailAsync(model.Username);

            if (user != null && !user.IsActive)
            {
                _logger.LogWarning("Intento de inicio de sesión rechazado: usuario {Username} inactivo", model.Username);
                return Redirect("/login?error=" + Uri.EscapeDataString("Su cuenta se encuentra inactiva. Contacte al administrador."));
            }

            // H14: Habilitar lockoutOnFailure: true para bloquear tras superar intentos fallidos
            var result = await _signInManager.PasswordSignInAsync(
                model.Username,
                model.Password,
                isPersistent: true,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("Usuario {Username} inició sesión correctamente", model.Username);
                return LocalRedirect(model.ReturnUrl ?? "/");
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("Cuenta de usuario {Username} bloqueada por intentos fallidos", model.Username);
                return Redirect("/login?error=" + Uri.EscapeDataString("Su cuenta ha sido bloqueada temporalmente por exceso de intentos fallidos."));
            }

            _logger.LogWarning("Intento de inicio de sesión inválido para {Username}", model.Username);
            return Redirect("/login?error=" + Uri.EscapeDataString("Credenciales incorrectas"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el inicio de sesión");
            return Redirect("/login?error=" + Uri.EscapeDataString("Error al iniciar sesión"));
        }
    }

    public class LoginRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    [HttpGet("Logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("Usuario cerró sesión.");
        return Redirect("/login");
    }
}
