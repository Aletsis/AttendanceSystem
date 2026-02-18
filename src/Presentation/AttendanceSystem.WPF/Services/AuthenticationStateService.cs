using System.Windows.Controls;

namespace AttendanceSystem.WPF.Services
{
    public interface IAuthenticationStateService
    {
        bool IsAuthenticated { get; }
        string? CurrentUserName { get; }
        Task<bool> LoginAsync(string username, string password);
        Task LogoutAsync();
    }

    public class AuthenticationStateService : IAuthenticationStateService
    {
        public bool IsAuthenticated { get; private set; }
        public string? CurrentUserName { get; private set; }

        public async Task<bool> LoginAsync(string username, string password)
        {
            await Task.Delay(100);
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                IsAuthenticated = true;
                CurrentUserName = username;
                return true;
            }
            return false;
        }

        public async Task LogoutAsync()
        {
            await Task.Delay(100);
            IsAuthenticated = false;
            CurrentUserName = null;
        }
    }
}
