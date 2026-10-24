using System.Security.Claims;
using AttendanceSystem.Application.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace AttendanceSystem.Blazor.Server.Services;

/// <summary>Caso 13: permisos del usuario del circuito para mostrar u ocultar controles desde el código de un componente.</summary>
public sealed class UiPermissions
{
    private readonly AuthenticationStateProvider _authState;

    public UiPermissions(AuthenticationStateProvider authState) => _authState = authState;

    public async Task<bool> HasAsync(string permission)
    {
        var user = (await _authState.GetAuthenticationStateAsync()).User;
        return user.Identity?.IsAuthenticated == true &&
               RolePermissions.Grants(user.FindAll(ClaimTypes.Role).Select(c => c.Value), permission);
    }
}
