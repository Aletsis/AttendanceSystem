using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels with common functionality
    /// </summary>
    public abstract class ViewModelBase : BindableBase
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

        protected void SetBusy(bool isBusy, string message = "Cargando...")
        {
            IsBusy = isBusy;
            BusyMessage = message;
        }
    }
}
