namespace AttendanceSystem.Application.Authorization;

/// <summary>Indica que el comando sólo puede ejecutarlo quien tenga el permiso (caso 13).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public RequiresPermissionAttribute(string permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>Permisos adicionales que dependen de los datos de la petición (p. ej. forzar la descarga completa).</summary>
public interface IRequiresDataPermissions
{
    IEnumerable<string> RequiredPermissions();
}

/// <summary>Permisos del usuario actual.</summary>
public interface IPermissionService
{
    /// <summary>
    /// Sin usuario autenticado la operación la ejecuta el sistema (tareas programadas, ADMS) y no se restringe:
    /// toda página y API de usuarios exige iniciar sesión.
    /// </summary>
    bool IsSystem { get; }

    bool Has(string permission);
}

public sealed class PermissionService : IPermissionService
{
    private readonly ICurrentUserService? _currentUser;

    /// <param name="currentUser">Nulo en hosts sin usuarios (servicio de relojes): todo lo ejecuta el sistema.</param>
    public PermissionService(ICurrentUserService? currentUser) => _currentUser = currentUser;

    public bool IsSystem => _currentUser is not { IsAuthenticated: true };

    public bool Has(string permission) => IsSystem || RolePermissions.Grants(_currentUser!.Roles, permission);
}

public static class PermissionServiceExtensions
{
    public const string DeniedMessage = "No tiene permiso para realizar esta acción.";

    /// <summary>Error si falta el permiso; <c>null</c> si lo tiene o no hay servicio de permisos (pruebas).</summary>
    public static string? Deny(this IPermissionService? permissions, string permission) =>
        permissions == null || permissions.Has(permission) ? null : DeniedMessage;
}
