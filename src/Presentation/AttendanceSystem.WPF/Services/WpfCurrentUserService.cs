namespace AttendanceSystem.WPF.Services;

using AttendanceSystem.Application.Abstractions;

public class WpfCurrentUserService : ICurrentUserService
{
    private readonly IAuthenticationStateService _authService;

    public WpfCurrentUserService(IAuthenticationStateService authService)
    {
        _authService = authService;
    }

    public string? UserId => _authService.CurrentUserId;

    public string? UserName => _authService.CurrentUserName;

    public bool IsAuthenticated => _authService.IsAuthenticated;

    public IReadOnlyCollection<string> Roles => _authService.CurrentUserRoles;

    /// <summary>En el cliente WPF todo lo hace un usuario: sin sesión iniciada no se permite ningún comando.</summary>
    public bool RequiresSignIn => true;
}
