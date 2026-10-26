using System.Security.Claims;
using AttendanceSystem.Application.Authorization;
using Hangfire.Dashboard;

namespace AttendanceSystem.Blazor.Server.Services;

/// <summary>Caso 13: el panel de Hangfire sólo lo abre quien tenga <see cref="Permissions.JobsView"/>, también por URL.</summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true &&
               RolePermissions.Grants(user.FindAll(ClaimTypes.Role).Select(c => c.Value), Permissions.JobsView);
    }
}
