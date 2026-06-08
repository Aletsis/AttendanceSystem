using System;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Branches
{
    public class BranchDetailViewModel : BindableBase, IDialogAware
    {
        private Guid? _branchId;
        private string _code = string.Empty;
        private string _name = string.Empty;
        private string? _address;
        private bool _isExternal;
        private string? _externalHost;
        private string _title = "Nueva Sucursal";

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Code
        {
            get => _code;
            set => SetProperty(ref _code, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string? Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        public bool IsExternal
        {
            get => _isExternal;
            set => SetProperty(ref _isExternal, value);
        }

        public string? ExternalHost
        {
            get => _externalHost;
            set => SetProperty(ref _externalHost, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<IDialogResult> RequestClose;

        public BranchDetailViewModel()
        {
            SaveCommand = new DelegateCommand(ExecuteSave, CanExecuteSave)
                .ObservesProperty(() => Code)
                .ObservesProperty(() => Name)
                .ObservesProperty(() => IsExternal)
                .ObservesProperty(() => ExternalHost);
            
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private bool CanExecuteSave()
        {
            if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
                return false;
            
            if (IsExternal && string.IsNullOrWhiteSpace(ExternalHost))
                return false;
            
            return true;
        }

        private void ExecuteSave()
        {
            var parameters = new DialogParameters
            {
                { "BranchId", _branchId },
                { "Code", Code },
                { "Name", Name },
                { "Address", Address },
                { "IsExternal", IsExternal },
                { "ExternalHost", ExternalHost }
            };

            RequestClose?.Invoke(new DialogResult(ButtonResult.OK, parameters));
        }

        private void ExecuteCancel()
        {
            RequestClose?.Invoke(new DialogResult(ButtonResult.Cancel));
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("BranchId"))
            {
                _branchId = parameters.GetValue<Guid>("BranchId");
                Code = parameters.GetValue<string>("Code");
                Name = parameters.GetValue<string>("Name");
                Address = parameters.GetValue<string>("Address");
                IsExternal = parameters.GetValue<bool>("IsExternal");
                ExternalHost = parameters.GetValue<string>("ExternalHost");
                Title = "Editar Sucursal";
            }
        }
    }
}
