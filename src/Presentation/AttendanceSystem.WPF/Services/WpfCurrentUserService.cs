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
}
