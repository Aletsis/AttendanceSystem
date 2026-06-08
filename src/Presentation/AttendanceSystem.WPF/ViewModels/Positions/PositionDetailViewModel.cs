using System;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Positions
{
    public class PositionDetailViewModel : BindableBase, IDialogAware
    {
        private Guid? _positionId;
        private string _name = string.Empty;
        private string? _description;
        private decimal _baseSalary;
        private string _title = "Nuevo Puesto";

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string? Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public decimal BaseSalary
        {
            get => _baseSalary;
            set => SetProperty(ref _baseSalary, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<IDialogResult> RequestClose;

        public PositionDetailViewModel()
        {
            SaveCommand = new DelegateCommand(ExecuteSave, CanExecuteSave)
                .ObservesProperty(() => Name);
            
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private bool CanExecuteSave()
        {
            return !string.IsNullOrWhiteSpace(Name);
        }

        private void ExecuteSave()
        {
            var parameters = new DialogParameters
            {
                { "PositionId", _positionId },
                { "Name", Name },
                { "Description", Description },
                { "BaseSalary", BaseSalary }
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
            if (parameters.ContainsKey("PositionId"))
            {
                _positionId = parameters.GetValue<Guid>("PositionId");
                Name = parameters.GetValue<string>("Name");
                Description = parameters.GetValue<string>("Description");
                BaseSalary = parameters.GetValue<decimal>("BaseSalary");
                Title = "Editar Puesto";
            }
        }
    }
}
