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
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager, 
        IAuthenticationService authService,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
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
            // Usar SignInManager directamente aquí porque estamos en un contexto HTTP tradicional
            var result = await _signInManager.PasswordSignInAsync(
                model.Username, 
                model.Password, 
                isPersistent: true, 
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation("Usuario {Username} inició sesión correctamente", model.Username);
                return LocalRedirect(model.ReturnUrl ?? "/");
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("Cuenta de usuario bloqueada");
                return Redirect("/login?error=" + Uri.EscapeDataString("Su cuenta está bloqueada"));
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
