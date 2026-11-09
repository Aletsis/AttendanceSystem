namespace AttendanceSystem.Application.Authorization;

/// <summary>
/// Usuario administrador que crea el sistema al instalarse. Es la llave de recuperación: no se puede eliminar,
/// desactivar ni quitarle el rol de administrador desde la aplicación. Si se olvida su contraseña, se restablece en el
/// servidor con <see cref="ResetPasswordArgument"/>.
/// </summary>
public static class BuiltInAdmin
{
    public const string UserName = "admin";

    /// <summary>Argumento del ejecutable del sitio que restablece la contraseña del administrador y termina.</summary>
    public const string ResetPasswordArgument = "--reset-admin-password";

    public static bool Is(string? userName) => string.Equals(userName, UserName, StringComparison.OrdinalIgnoreCase);
}
