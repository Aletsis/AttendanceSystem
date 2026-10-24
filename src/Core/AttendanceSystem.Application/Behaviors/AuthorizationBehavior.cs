using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.Exceptions;

namespace AttendanceSystem.Application.Behaviors;

/// <summary>
/// Caso 13: rechaza una petición marcada con <see cref="RequiresPermissionAttribute"/> si el usuario no tiene el
/// permiso. Sólo se revisa la petición de entrada: lo que un comando autorizado envía a su vez (p. ej. el recálculo
/// tras una descarga) forma parte de la misma operación. Las peticiones que devuelven <see cref="Result"/> reciben un
/// fallo; las demás, <see cref="ForbiddenAccessException"/>.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IPermissionService _permissions;

    public AuthorizationBehavior(IPermissionService permissions) => _permissions = permissions;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (AuthorizedOperation.Active.Value) return await next();

        var required = (RequiresPermissionAttribute?)Attribute.GetCustomAttribute(typeof(TRequest), typeof(RequiresPermissionAttribute));
        if (required != null && !_permissions.Has(required.Permission))
        {
            return Deny();
        }
        if (request is IRequiresDataPermissions data && data.RequiredPermissions().Any(p => !_permissions.Has(p)))
        {
            return Deny();
        }

        AuthorizedOperation.Active.Value = true;
        try
        {
            return await next();
        }
        finally
        {
            AuthorizedOperation.Active.Value = false;
        }
    }

    private static TResponse Deny()
    {
        const string error = PermissionServiceExtensions.DeniedMessage;
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failure = typeof(TResponse).GetMethod(nameof(Result.Failure), new[] { typeof(string) })!;
            return (TResponse)failure.Invoke(null, new object[] { error })!;
        }
        throw new ForbiddenAccessException(error);
    }
}

/// <summary>Una sola marca para todos los tipos de petición (un campo estático de la clase genérica sería uno por tipo).</summary>
internal static class AuthorizedOperation
{
    public static readonly AsyncLocal<bool> Active = new();
}
