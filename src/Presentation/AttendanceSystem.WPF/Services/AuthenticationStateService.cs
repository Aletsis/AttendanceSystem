using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.WPF.Services
{
    public interface IAuthenticationStateService
    {
        bool IsAuthenticated { get; }
        string? CurrentUserId { get; }
        string? CurrentUserName { get; }
        string? CurrentUserRole { get; }
        IReadOnlyCollection<string> CurrentUserRoles { get; }
        /// <summary>Caso 13: permisos del usuario según sus roles (ver RolePermissions).</summary>
        bool HasPermission(string permission);
        Task<bool> LoginAsync(string username, string password);
        Task LogoutAsync();
    }

    /// <summary>
    /// Sesión del cliente WPF (singleton). El UserManager se obtiene en un ámbito propio en cada inicio de sesión: es
    /// scoped (usa el DbContext) y el DbContext depende del usuario actual, que depende de este servicio; recibirlo en
    /// el constructor formaba un ciclo de dependencias y retenía un DbContext durante toda la aplicación.
    /// </summary>
    public class AuthenticationStateService : IAuthenticationStateService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public bool IsAuthenticated { get; private set; }
        public string? CurrentUserId { get; private set; }
        public string? CurrentUserName { get; private set; }
        public string? CurrentUserRole { get; private set; }
        public IReadOnlyCollection<string> CurrentUserRoles { get; private set; } = Array.Empty<string>();

        public bool HasPermission(string permission) =>
            IsAuthenticated && AttendanceSystem.Application.Authorization.RolePermissions.Grants(CurrentUserRoles, permission);

        public AuthenticationStateService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Buscar por UserName (normalmente el email o el nombre de usuario registrado)
            var user = await userManager.FindByNameAsync(username)
                       ?? await userManager.FindByEmailAsync(username);

            if (user == null || !user.IsActive)
                return false;

            // H14: Validar si la cuenta está bloqueada por intentos fallidos previos
            if (await userManager.IsLockedOutAsync(user))
                return false;

            var result = userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash!, password);

            if (result == PasswordVerificationResult.Failed)
            {
                // Incrementar contador de accesos fallidos (activa bloqueo si alcanza el límite)
                await userManager.AccessFailedAsync(user);
                return false;
            }

            // Restablecer contador de accesos fallidos tras autenticación exitosa
            await userManager.ResetAccessFailedCountAsync(user);

            // Si el hash necesita actualización (PasswordVerificationResult.SuccessRehashNeeded)
            // lo marcamos igual como éxito; el rehash se haría en la siguiente operación.
            IsAuthenticated = true;
            CurrentUserId = user.Id;
            CurrentUserName = user.UserName;

            // Obtener rol principal para futuro control de acceso
            var roles = await userManager.GetRolesAsync(user);
            CurrentUserRole = roles.FirstOrDefault();
            CurrentUserRoles = roles.ToList();

            return true;
        }

        public Task LogoutAsync()
        {
            IsAuthenticated = false;
            CurrentUserId = null;
            CurrentUserName = null;
            CurrentUserRole = null;
            CurrentUserRoles = Array.Empty<string>();
            return Task.CompletedTask;
        }
    }
}
