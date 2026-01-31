using Microsoft.AspNetCore.Identity;

namespace AttendanceSystem.Domain.Entities;

/// <summary>
/// Representa un usuario del sistema con capacidades de autenticación.
/// Extiende IdentityUser para aprovechar las funcionalidades de ASP.NET Core Identity.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Nombre completo del usuario
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Indica si el usuario está activo en el sistema
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Fecha de creación del usuario
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Fecha de última modificación
    /// </summary>
    public DateTime? LastModifiedAt { get; set; }
}
