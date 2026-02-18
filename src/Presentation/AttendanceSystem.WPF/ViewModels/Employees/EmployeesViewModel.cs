using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    public class EmployeesViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private ObservableCollection<EmployeeListItem> _employees = new();
        private ObservableCollection<EmployeeListItem> _filteredEmployees = new();
        private EmployeeListItem? _selectedEmployee;
        private string _searchText = string.Empty;
        private string _selectedStatus = "Todos";
        private List<EmployeeDto> _allEmployeesData = new();

        public ObservableCollection<EmployeeListItem> Employees
        {
            get => _filteredEmployees;
            set => SetProperty(ref _filteredEmployees, value);
        }

        public EmployeeListItem? SelectedEmployee
        {
            get => _selectedEmployee;
            set => SetProperty(ref _selectedEmployee, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterEmployees();
                }
            }
        }

        public string SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                if (SetProperty(ref _selectedStatus, value))
                {
                    FilterEmployees();
                }
            }
        }

        public List<string> StatusOptions { get; } = new() { "Todos", "Alta", "Baja" };

        public ICommand AddEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public EmployeesViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            AddEmployeeCommand = new DelegateCommand(ExecuteAddEmployee);
            EditEmployeeCommand = new DelegateCommand(ExecuteEditEmployee, CanExecuteEditEmployee)
                .ObservesProperty(() => SelectedEmployee);
            DeleteEmployeeCommand = new DelegateCommand(async () => await ExecuteDeleteEmployeeAsync(), CanExecuteEditEmployee)
                .ObservesProperty(() => SelectedEmployee);
            RefreshCommand = new DelegateCommand(async () => await LoadEmployeesAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            SetBusy(true, "Cargando empleados...");
            try
            {
                var result = await _mediator.Send(new GetAllEmployeesQuery());
                
                if (result.IsSuccess && result.Value != null)
                {
                    _allEmployeesData = result.Value.ToList();
                    _employees.Clear();
                    
                    foreach (var emp in _allEmployeesData)
                    {
                        _employees.Add(new EmployeeListItem
                        {
                            Id = emp.Id,
                            EmployeeNumber = emp.Id,
                            FullName = emp.FullName,
                            Email = emp.Email,
                            Phone = emp.PhoneNumber ?? "N/A",
                            DepartmentName = emp.DepartmentName,
                            PositionName = emp.PositionName,
                            BranchName = emp.BranchName,
                            Status = emp.Status == EmployeeStatus.Alta ? "Alta" : "Baja",
                            HireDate = emp.HireDate
                        });
                    }
                    
                    FilterEmployees();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar empleados: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar empleados: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterEmployees()
        {
            var query = _employees.AsEnumerable();

            // Filtrar por búsqueda
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchLower = SearchText.ToLower();
                query = query.Where(e =>
                    e.FullName.ToLower().Contains(searchLower) ||
                    e.EmployeeNumber.ToLower().Contains(searchLower) ||
                    e.Email.ToLower().Contains(searchLower) ||
                    e.DepartmentName.ToLower().Contains(searchLower) ||
                    e.PositionName.ToLower().Contains(searchLower));
            }

            // Filtrar por estado
            if (SelectedStatus != "Todos")
            {
                query = query.Where(e => e.Status == SelectedStatus);
            }

            Employees = new ObservableCollection<EmployeeListItem>(query);
        }

        private void ExecuteAddEmployee()
        {
            var parameters = new Prism.Regions.NavigationParameters();
            _navigationService.NavigateTo("EmployeeDetailView", parameters);
        }

        private void ExecuteEditEmployee()
        {
            if (SelectedEmployee == null) return;
            
            var parameters = new Prism.Regions.NavigationParameters();
            parameters.Add("EmployeeId", SelectedEmployee.Id);
            
            _navigationService.NavigateTo("EmployeeDetailView", parameters);
        }

        private async Task ExecuteDeleteEmployeeAsync()
        {
            if (SelectedEmployee == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar al empleado {SelectedEmployee.FullName}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando empleado...");
            try
            {
                var command = new DeleteEmployeeCommand(SelectedEmployee.EmployeeNumber);
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Empleado eliminado correctamente");
                    await LoadEmployeesAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al eliminar empleado: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al eliminar empleado: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool CanExecuteEditEmployee()
        {
            return SelectedEmployee != null;
        }
    }

    public class EmployeeListItem
    {
        public string Id { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string PositionName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime HireDate { get; set; }
    }
}
