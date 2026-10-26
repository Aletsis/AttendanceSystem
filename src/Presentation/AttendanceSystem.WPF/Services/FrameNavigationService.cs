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
    }

    /// <summary>
    /// Navegación de la región principal. Sin sesión iniciada sólo se muestra el inicio de sesión: sin usuario, los
    /// comandos se tratan como del sistema y no se restringen (caso 13), así que ninguna otra pantalla debe abrirse.
    /// </summary>
    public class FrameNavigationService : IFrameNavigationService
    {
        private readonly IRegionManager _regionManager;
        private readonly IAuthenticationStateService? _authService;
        private const string MainRegionName = "MainRegion";
        private static readonly string LoginViewName = nameof(Views.Auth.LoginView);

        public FrameNavigationService(IRegionManager regionManager, IAuthenticationStateService? authService = null)
        {
            _regionManager = regionManager;
            _authService = authService;
        }

        private string Allowed(string viewName) =>
            _authService is { IsAuthenticated: false } ? LoginViewName : viewName;

        public void NavigateTo<T>() where T : class
        {
            NavigateTo(typeof(T).Name);
        }

        public void NavigateTo(string viewName)
        {
            _regionManager.RequestNavigate(MainRegionName, Allowed(viewName));
        }

        public void NavigateTo<T>(NavigationParameters parameters) where T : class
        {
            NavigateTo(typeof(T).Name, parameters);
        }

        public void NavigateTo(string viewName, NavigationParameters parameters)
        {
            var allowed = Allowed(viewName);
            if (allowed == viewName) _regionManager.RequestNavigate(MainRegionName, viewName, parameters);
            else _regionManager.RequestNavigate(MainRegionName, allowed);
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
    }
}
