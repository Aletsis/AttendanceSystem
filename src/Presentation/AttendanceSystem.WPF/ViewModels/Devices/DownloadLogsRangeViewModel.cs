using System;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Devices
{
    public class DownloadLogsRangeViewModel : BindableBase, IDialogAware
    {
        private DateTime? _fromDate;
        private DateTime? _toDate;

        public DateTime? FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
        public DateTime? ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }

        public ICommand DownloadCommand { get; }
        public ICommand CancelCommand { get; }

        public string Title => "Descargar Registros";

        public event Action<IDialogResult>? RequestClose;

        public DownloadLogsRangeViewModel()
        {
            DownloadCommand = new DelegateCommand(ExecuteDownload);
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private void ExecuteDownload()
        {
            var parameters = new DialogParameters
            {
                { "FromDate", FromDate },
                { "ToDate", ToDate }
            };
            RequestClose?.Invoke(new DialogResult(ButtonResult.OK, parameters));
        }

        private void ExecuteCancel()
        {
            RequestClose?.Invoke(new DialogResult(ButtonResult.Cancel));
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) { }
    }
}
