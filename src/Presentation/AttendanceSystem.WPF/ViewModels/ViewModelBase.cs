using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace AttendanceSystem.WPF.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels with common functionality
    /// </summary>
    public abstract class ViewModelBase : BindableBase, IRegionMemberLifetime
    {
        private bool _isBusy;
        private string _busyMessage = "Cargando...";

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string BusyMessage
        {
            get => _busyMessage;
            set => SetProperty(ref _busyMessage, value);
        }

        /// <summary>
        /// Las pantallas no se conservan al salir de ellas: al volver se crean de nuevo y cargan datos actuales, y al
        /// cambiar de usuario no queda ninguna pantalla (ni sus datos) de la sesión anterior.
        /// </summary>
        public bool KeepAlive => false;

        protected void SetBusy(bool isBusy, string message = "Cargando...")
        {
            IsBusy = isBusy;
            BusyMessage = message;
        }
    }
}
