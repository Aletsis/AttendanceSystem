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
            if (IsBusy) return;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
            {
                await _messageService.ShowErrorAsync("Por favor ingrese usuario y contraseña.");
                return;
            }

            SetBusy(true, "Iniciando sesión...");
            try
            {
                var result = await _authService.LoginAsync(Username.Trim(), password);
                // La contraseña no se conserva en la pantalla, ni siquiera si falla.
                passwordBox?.Clear();

                switch (result)
                {
                    case LoginResult.Success:
                        // Se empieza con la región y el historial vacíos: nada de la sesión anterior queda a mano.
                        _navigationService.ResetTo(nameof(Views.Dashboard.DashboardView));
                        break;
                    case LoginResult.LockedOut:
                        await _messageService.ShowErrorAsync("Su cuenta ha sido bloqueada temporalmente por exceso de intentos fallidos.");
                        break;
                    default:
                        await _messageService.ShowErrorAsync("Credenciales incorrectas.");
                        break;
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
