using Microsoft.AspNetCore.Identity;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.WPF.Services
{
    public interface IAuthenticationStateService
    {
        bool IsAuthenticated { get; }
        string? CurrentUserName { get; }
        string? CurrentUserRole { get; }
        Task<bool> LoginAsync(string username, string password);
        Task LogoutAsync();
    }

    public class AuthenticationStateService : IAuthenticationStateService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public bool IsAuthenticated { get; private set; }
        public string? CurrentUserName { get; private set; }
        public string? CurrentUserRole { get; private set; }

        public AuthenticationStateService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            // Buscar por UserName (normalmente el email o el nombre de usuario registrado)
            var user = await _userManager.FindByNameAsync(username)
                       ?? await _userManager.FindByEmailAsync(username);

            if (user == null || !user.IsActive)
                return false;

            var result = _userManager.PasswordHasher.VerifyHashedPassword(
                user, user.PasswordHash!, password);

            if (result == PasswordVerificationResult.Failed)
                return false;

            // Si el hash necesita actualización (PasswordVerificationResult.SuccessRehashNeeded)
            // lo marcamos igual como éxito; el rehash se haría en la siguiente operación.
            IsAuthenticated = true;
            CurrentUserName = user.UserName;

            // Obtener rol principal para futuro control de acceso
            var roles = await _userManager.GetRolesAsync(user);
            CurrentUserRole = roles.FirstOrDefault();

            return true;
        }

        public Task LogoutAsync()
        {
            IsAuthenticated = false;
            CurrentUserName = null;
            CurrentUserRole = null;
            return Task.CompletedTask;
        }
    }
}
