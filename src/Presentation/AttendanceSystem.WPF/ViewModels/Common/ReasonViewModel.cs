using System.Windows.Input;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Common
{
    /// <summary>Motivo obligatorio de un ajuste manual. Parámetros: "Title" y "Message"; devuelve "Reason".</summary>
    public class ReasonViewModel : BindableBase, IDialogAware
    {
        private string _title = "Motivo";
        private string _message = string.Empty;
        private string _reason = string.Empty;

        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }

        public string Reason
        {
            get => _reason;
            set
            {
                if (SetProperty(ref _reason, value)) ((DelegateCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public DialogCloseListener RequestClose { get; }

        public ReasonViewModel()
        {
            SaveCommand = new DelegateCommand(
                () => RequestClose.Invoke(new DialogParameters { { "Reason", Reason.Trim() } }, ButtonResult.OK),
                () => !string.IsNullOrWhiteSpace(Reason));
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Title")) Title = parameters.GetValue<string>("Title");
            if (parameters.ContainsKey("Message")) Message = parameters.GetValue<string>("Message");
        }
    }
}
