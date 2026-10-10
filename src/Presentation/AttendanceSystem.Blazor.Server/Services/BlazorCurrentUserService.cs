namespace AttendanceSystem.Blazor.Server.Services;

using System.Security.Claims;
using AttendanceSystem.Application.Abstractions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

public class BlazorCurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BlazorCurrentUserService(
        AuthenticationStateProvider authStateProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _authStateProvider = authStateProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId
    {
        get
        {
            var user = GetUser();
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }

    public string? UserName
    {
        get
        {
            var user = GetUser();
            return user?.Identity?.Name;
        }
    }

    public bool IsAuthenticated => GetUser()?.Identity?.IsAuthenticated == true;

    private ClaimsPrincipal? GetUser()
    {
        if (_httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            return _httpContextAccessor.HttpContext.User;
        }

        try
        {
            var task = _authStateProvider.GetAuthenticationStateAsync();
            if (task.IsCompletedSuccessfully)
            {
                return task.Result.User;
            }
            return task.GetAwaiter().GetResult().User;
        }
        catch
        {
            return null;
        }
    }
}
