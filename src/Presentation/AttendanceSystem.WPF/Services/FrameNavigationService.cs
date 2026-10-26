using Prism.Navigation.Regions;

namespace AttendanceSystem.WPF.Services
{
    public interface IFrameNavigationService
    {
        void NavigateTo<T>() where T : class;
        void NavigateTo(string viewName);
        void NavigateTo<T>(NavigationParameters parameters) where T : class;
        void NavigateTo(string viewName, NavigationParameters parameters);
        void GoBack();
        bool CanGoBack { get; }

        /// <summary>
        /// Descarta todas las pantallas abiertas y el historial y muestra el inicio de sesión. Se usa al iniciar y al
        /// cerrar sesión: nada de la sesión anterior (datos, contraseña escrita, historial) debe quedar disponible.
        /// </summary>
        void ResetTo(string viewName);
    }

    /// <summary>
    /// Navegación de la región principal. Sin sesión iniciada sólo se muestra el inicio de sesión: sin usuario, los
    /// comandos se tratan como del sistema y no se restringen (caso 13), así que ninguna otra pantalla debe abrirse.
    /// Con sesión, cada pantalla exige su permiso (<see cref="PagePermissions"/>).
    /// </summary>
    public class FrameNavigationService : IFrameNavigationService
    {
        public const string MainRegionName = "MainRegion";
        public static readonly string LoginViewName = nameof(Views.Auth.LoginView);

        private readonly IRegionManager _regionManager;
        private readonly IAuthenticationStateService? _authService;
        private readonly IMessageService? _messageService;

        public FrameNavigationService(IRegionManager regionManager, IAuthenticationStateService? authService = null, IMessageService? messageService = null)
        {
            _regionManager = regionManager;
            _authService = authService;
            _messageService = messageService;
        }

        /// <summary>La pantalla pedida, el inicio de sesión si no hay sesión, o null si el usuario no tiene permiso.</summary>
        private string? Allowed(string viewName)
        {
            if (_authService == null) return viewName;
            if (!_authService.IsAuthenticated) return LoginViewName;
            if (viewName == LoginViewName || _authService.CanOpen(viewName)) return viewName;

            _ = _messageService?.ShowWarningAsync("No tiene permiso para abrir esta pantalla.");
            return null;
        }

        public void NavigateTo<T>() where T : class
        {
            NavigateTo(typeof(T).Name);
        }

        public void NavigateTo(string viewName)
        {
            if (Allowed(viewName) is { } allowed) _regionManager.RequestNavigate(MainRegionName, allowed);
        }

        public void NavigateTo<T>(NavigationParameters parameters) where T : class
        {
            NavigateTo(typeof(T).Name, parameters);
        }

        public void NavigateTo(string viewName, NavigationParameters parameters)
        {
            var allowed = Allowed(viewName);
            if (allowed == viewName) _regionManager.RequestNavigate(MainRegionName, viewName, parameters);
            else if (allowed != null) _regionManager.RequestNavigate(MainRegionName, allowed);
        }

        public void GoBack()
        {
            if (_authService is { IsAuthenticated: false })
            {
                NavigateTo(LoginViewName);
                return;
            }
            if (CanGoBack)
            {
                _regionManager.Regions[MainRegionName].NavigationService.Journal.GoBack();
            }
        }

        public bool CanGoBack => _regionManager.Regions.ContainsRegionWithName(MainRegionName) &&
                                 _regionManager.Regions[MainRegionName].NavigationService.Journal.CanGoBack;

        public void ResetTo(string viewName)
        {
            if (_regionManager.Regions.ContainsRegionWithName(MainRegionName))
            {
                // Con el historial vacío la pantalla de destino es la primera: "atrás" no vuelve a la sesión anterior.
                var region = _regionManager.Regions[MainRegionName];
                region.RemoveAll();
                region.NavigationService.Journal.Clear();
            }

            _regionManager.RequestNavigate(MainRegionName, Allowed(viewName) ?? LoginViewName);
        }
    }
}
