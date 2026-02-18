using Prism.Regions;

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

    public class FrameNavigationService : IFrameNavigationService
    {
        private readonly IRegionManager _regionManager;
        private const string MainRegionName = "MainRegion";

        public FrameNavigationService(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        public void NavigateTo<T>() where T : class
        {
            NavigateTo(typeof(T).Name);
        }

        public void NavigateTo(string viewName)
        {
            _regionManager.RequestNavigate(MainRegionName, viewName);
        }

        public void NavigateTo<T>(NavigationParameters parameters) where T : class
        {
            NavigateTo(typeof(T).Name, parameters);
        }

        public void NavigateTo(string viewName, NavigationParameters parameters)
        {
            _regionManager.RequestNavigate(MainRegionName, viewName, parameters);
        }

        public void GoBack()
        {
            if (CanGoBack)
            {
                _regionManager.Regions[MainRegionName].NavigationService.Journal.GoBack();
            }
        }

        public bool CanGoBack => _regionManager.Regions.ContainsRegionWithName(MainRegionName) &&
                                 _regionManager.Regions[MainRegionName].NavigationService.Journal.CanGoBack;
    }
}
