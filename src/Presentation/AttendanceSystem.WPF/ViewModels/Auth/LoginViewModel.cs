using System.Windows.Input;
using System.Windows.Controls;
using Prism.Commands;
using AttendanceSystem.WPF.Services;

namespace AttendanceSystem.WPF.ViewModels.Auth
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IAuthenticationStateService _authService;
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;

        private string _username = "";

        public string Username 
        { 
            get => _username; 
            set 
            {
                SetProperty(ref _username, value);
                ((DelegateCommand<object>)LoginCommand).RaiseCanExecuteChanged();
            }
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel(
            IAuthenticationStateService authService, 
            IFrameNavigationService navigationService,
            IMessageService messageService)
        {
            _authService = authService;
            _navigationService = navigationService;
            _messageService = messageService;

            LoginCommand = new DelegateCommand<object>(async (p) => await ExecuteLoginAsync(p));
        }

        private async Task ExecuteLoginAsync(object parameter)
        {
            var passwordBox = parameter as PasswordBox;
            var password = passwordBox?.Password;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
            {
                await _messageService.ShowErrorAsync("Por favor ingrese usuario y contraseña.");
                return;
            }

            SetBusy(true, "Iniciando sesión...");
            try
            {
                var success = await _authService.LoginAsync(Username, password);
                if (success)
                {
                    // Navigate only if login succeeds
                    // Use literal View Name if T is not registered with exact name or if relying on Prism
                    // But assuming FrameNavigationService uses typeof(T).Name
                    _navigationService.NavigateTo<Views.Dashboard.DashboardView>();
                }
                else
                {
                     await _messageService.ShowErrorAsync("Credenciales incorrectas.");
                }
            }
            catch (Exception ex)
            {
                 await _messageService.ShowErrorAsync($"Error al iniciar sesión: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }
    }
}
