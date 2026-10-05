using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Domain.Enumerations;
using MediatR;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    public class ExternalEmployeeDetailViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private Guid? _id;
        private string _title = "Nuevo Empleado Externo";
        private BranchLookupItem? _selectedBranch;
        private string _employeeNumber = string.Empty;
        private string _firstName = string.Empty;
        private string _lastName = string.Empty;
        private string? _email;
        private string? _phoneNumber;
        private string? _position;
        private string? _department;
        private string? _cardNumber;
        private EmployeeStatus _status = EmployeeStatus.Alta;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public BranchLookupItem? SelectedBranch
        {
            get => _selectedBranch;
            set
            {
                if (SetProperty(ref _selectedBranch, value))
                {
                    (SaveCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<BranchLookupItem> ExternalBranches { get; } = new();

        public string EmployeeNumber
        {
            get => _employeeNumber;
            set
            {
                if (SetProperty(ref _employeeNumber, value))
                {
                    (SaveCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string FirstName
        {
            get => _firstName;
            set
            {
                if (SetProperty(ref _firstName, value))
                {
                    (SaveCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string LastName
        {
            get => _lastName;
            set
            {
                if (SetProperty(ref _lastName, value))
                {
                    (SaveCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        public string? PhoneNumber
        {
            get => _phoneNumber;
            set => SetProperty(ref _phoneNumber, value);
        }

        public string? Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        public string? Department
        {
            get => _department;
            set => SetProperty(ref _department, value);
        }

        public string? CardNumber
        {
            get => _cardNumber;
            set => SetProperty(ref _cardNumber, value);
        }

        public EmployeeStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public List<EmployeeStatus> StatusOptions { get; } = new()
        {
            EmployeeStatus.Alta,
            EmployeeStatus.Baja,
            EmployeeStatus.BajaTemporal,
            EmployeeStatus.Vacaciones,
            EmployeeStatus.Incapacidad
        };

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; }

        public ExternalEmployeeDetailViewModel(IMediator mediator)
        {
            _mediator = mediator;

            SaveCommand = new DelegateCommand(ExecuteSave, CanExecuteSave);
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private bool CanExecuteSave()
        {
            return SelectedBranch != null &&
                   !string.IsNullOrWhiteSpace(EmployeeNumber) &&
                   !string.IsNullOrWhiteSpace(FirstName) &&
                   !string.IsNullOrWhiteSpace(LastName);
        }

        private void ExecuteSave()
        {
            var parameters = new DialogParameters
            {
                { "BranchId", SelectedBranch!.Id },
                { "EmployeeNumber", EmployeeNumber.Trim() },
                { "FirstName", FirstName.Trim() },
                { "LastName", LastName.Trim() },
                { "Email", Email?.Trim() },
                { "PhoneNumber", PhoneNumber?.Trim() },
                { "Position", Position?.Trim() },
                { "Department", Department?.Trim() },
                { "Status", Status },
                { "CardNumber", CardNumber?.Trim() }
            };

            if (_id.HasValue)
            {
                parameters.Add("Id", _id.Value);
            }

            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        private void ExecuteCancel()
        {
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public async void OnDialogOpened(IDialogParameters parameters)
        {
            ExternalBranches.Clear();

            try
            {
                var branchesResult = await _mediator.Send(new GetBranchesQuery());
                if (branchesResult.IsSuccess && branchesResult.Value != null)
                {
                    foreach (var b in branchesResult.Value.Where(b => b.IsExternal))
                    {
                        ExternalBranches.Add(new BranchLookupItem
                        {
                            Id = b.Id,
                            Code = b.Code,
                            Name = b.Name
                        });
                    }
                }
            }
            catch
            {
                // Manejo silencioso en carga inicial
            }

            if (parameters.ContainsKey("Id"))
            {
                _id = parameters.GetValue<Guid>("Id");
                var branchId = parameters.GetValue<Guid>("BranchId");
                SelectedBranch = ExternalBranches.FirstOrDefault(b => b.Id == branchId);
                EmployeeNumber = parameters.GetValue<string>("EmployeeNumber");
                FirstName = parameters.GetValue<string>("FirstName");
                LastName = parameters.GetValue<string>("LastName");
                Email = parameters.GetValue<string?>("Email");
                PhoneNumber = parameters.GetValue<string?>("PhoneNumber");
                Position = parameters.GetValue<string?>("Position");
                Department = parameters.GetValue<string?>("Department");
                Status = parameters.GetValue<EmployeeStatus>("Status");
                CardNumber = parameters.GetValue<string?>("CardNumber");
                Title = "Editar Empleado Externo";
            }
            else
            {
                if (parameters.ContainsKey("BranchId"))
                {
                    var branchId = parameters.GetValue<Guid>("BranchId");
                    SelectedBranch = ExternalBranches.FirstOrDefault(b => b.Id == branchId);
                }
                else if (ExternalBranches.Count > 0)
                {
                    SelectedBranch = ExternalBranches[0];
                }
            }
        }
    }

    public class BranchLookupItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName => $"[{Code}] {Name}";
    }
}
