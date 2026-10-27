using System.Windows.Input;
using AttendanceSystem.Application.Features.Users.Commands.ChangeOwnPassword;
using MediatR;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Auth
{
    /// <summary>Caso 13: cualquier usuario cambia su propia contraseña (mismo comando que la versión web).</summary>
    public class ChangePasswordViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private string? _errorMessage;
        private bool _isSaving;

        public ChangePasswordViewModel(IMediator mediator)
        {
            _mediator = mediator;
            SaveCommand = new DelegateCommand(async () => await SaveAsync());
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;

        public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public DialogCloseListener RequestClose { get; }

        private async Task SaveAsync()
        {
            if (_isSaving) return;
            if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword))
            {
                ErrorMessage = "Escriba la contraseña actual y la nueva.";
                return;
            }
            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "La confirmación no coincide con la nueva contraseña.";
                return;
            }

            _isSaving = true;
            ErrorMessage = null;
            try
            {
                var result = await _mediator.Send(new ChangeOwnPasswordCommand(CurrentPassword, NewPassword));
                if (result.IsSuccess)
                {
                    // Antes de cerrar: CanCloseDialog rechaza el cierre mientras se guarda.
                    _isSaving = false;
                    RequestClose.Invoke(ButtonResult.OK);
                }
                else ErrorMessage = result.Error;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"No se pudo cambiar la contraseña: {ex.Message}";
            }
            finally
            {
                _isSaving = false;
            }
        }

        public bool CanCloseDialog() => !_isSaving;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) { }
    }
}
