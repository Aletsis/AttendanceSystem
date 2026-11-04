namespace AttendanceSystem.WPF.Services;

using AttendanceSystem.Application.Abstractions;

/// <summary>
/// Marca el ámbito de una operación pedida desde la interfaz (<see cref="WpfScopedMediator"/>). Lo demás que corre en el
/// proceso (tareas programadas, consolidación, outbox, receptor ADMS) no la tiene y actúa como Sistema, igual que en
/// el sitio web: no depende de que haya una sesión abierta ni se atribuye a quien la tenga.
/// </summary>
public sealed class WpfOperationScope
{
    public bool RequestedByUser { get; set; }
}

public class WpfCurrentUserService : ICurrentUserService
{
    private readonly IAuthenticationStateService _authService;
    private readonly WpfOperationScope _scope;

    public WpfCurrentUserService(IAuthenticationStateService authService, WpfOperationScope scope)
    {
        _authService = authService;
        _scope = scope;
    }

    public string? UserId => _scope.RequestedByUser ? _authService.CurrentUserId : null;

    public string? UserName => _scope.RequestedByUser ? _authService.CurrentUserName : null;

    public bool IsAuthenticated => _scope.RequestedByUser && _authService.IsAuthenticated;

    public IReadOnlyCollection<string> Roles => _scope.RequestedByUser ? _authService.CurrentUserRoles : Array.Empty<string>();

    /// <summary>Lo que pide la interfaz lo hace un usuario: sin sesión iniciada no se permite ningún comando.</summary>
    public bool RequiresSignIn => _scope.RequestedByUser;
}
