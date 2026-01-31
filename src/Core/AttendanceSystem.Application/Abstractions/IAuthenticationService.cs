namespace AttendanceSystem.Application.Abstractions;

/// <summary>
/// Servicio de autenticación para gestionar el inicio de sesión y cierre de sesión de usuarios
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Autentica un usuario con sus credenciales
    /// </summary>
    /// <param name="username">Nombre de usuario</param>
    /// <param name="password">Contraseña</param>
    /// <returns>True si la autenticación fue exitosa, false en caso contrario</returns>
    Task<bool> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Verifica si un usuario tiene rol de administrador
    /// </summary>
    /// <param name="username">Nombre de usuario</param>
    /// <returns>True si el usuario es administrador, false en caso contrario</returns>
    Task<bool> IsAdministratorAsync(string username);

    /// <summary>
    /// Cierra la sesión del usuario actual
    /// </summary>
    Task LogoutAsync();
}
