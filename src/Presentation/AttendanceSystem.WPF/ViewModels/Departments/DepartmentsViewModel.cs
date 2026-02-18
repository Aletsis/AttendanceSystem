using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Departments.Commands.DeleteDepartment;
using AttendanceSystem.Application.Features.Departments;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.WPF.ViewModels.Departments
{
    public class DepartmentsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private ObservableCollection<DepartmentListItem> _departments = new();
        private ObservableCollection<DepartmentListItem> _filteredDepartments = new();
        private DepartmentListItem? _selectedDepartment;
        private string _searchText = string.Empty;
        private List<DepartmentDto> _allDepartmentsData = new();

        public ObservableCollection<DepartmentListItem> Departments
        {
            get => _filteredDepartments;
            set => SetProperty(ref _filteredDepartments, value);
        }

        public DepartmentListItem? SelectedDepartment
        {
            get => _selectedDepartment;
            set => SetProperty(ref _selectedDepartment, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterDepartments();
                }
            }
        }

        public ICommand AddDepartmentCommand { get; }
        public ICommand EditDepartmentCommand { get; }
        public ICommand DeleteDepartmentCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public DepartmentsViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            AddDepartmentCommand = new DelegateCommand(ExecuteAddDepartment);
            EditDepartmentCommand = new DelegateCommand(ExecuteEditDepartment, CanExecuteEdit)
                .ObservesProperty(() => SelectedDepartment);
            DeleteDepartmentCommand = new DelegateCommand(async () => await ExecuteDeleteDepartmentAsync(), CanExecuteEdit)
                .ObservesProperty(() => SelectedDepartment);
            RefreshCommand = new DelegateCommand(async () => await LoadDepartmentsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadDepartmentsAsync();
        }

        private async Task LoadDepartmentsAsync()
        {
            SetBusy(true, "Cargando departamentos...");
            try
            {
                var result = await _mediator.Send(new GetDepartmentsQuery());
                
                if (result.IsSuccess && result.Value != null)
                {
                    _allDepartmentsData = result.Value.ToList();
                    _departments.Clear();
                    
                    foreach (var dept in _allDepartmentsData)
                    {
                        _departments.Add(new DepartmentListItem
                        {
                            Id = dept.Id,
                            Name = dept.Name,
                            Description = dept.Description,
                            EmployeeCount = 0 // TODO: Get count from query if needed
                        });
                    }
                    
                    FilterDepartments();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar departamentos: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar departamentos: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterDepartments()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Departments = new ObservableCollection<DepartmentListItem>(_departments);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _departments.Where(d => 
                    d.Name.ToLower().Contains(searchLower) || 
                    (d.Description?.ToLower().Contains(searchLower) ?? false));
                Departments = new ObservableCollection<DepartmentListItem>(filtered);
            }
        }

        private void ExecuteAddDepartment()
        {
            _messageService.ShowMessageAsync("Agregar Departamento", "Formulario de creación en desarrollo...");
        }

        private void ExecuteEditDepartment()
        {
            if (SelectedDepartment == null) return;
            _messageService.ShowMessageAsync("Editar Departamento", $"Formulario de edición para {SelectedDepartment.Name} en desarrollo...");
        }

        private async Task ExecuteDeleteDepartmentAsync()
        {
            if (SelectedDepartment == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar el departamento {SelectedDepartment.Name}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando departamento...");
            try
            {
                var command = new DeleteDepartmentCommand(SelectedDepartment.Id);
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Departamento eliminado correctamente");
                    await LoadDepartmentsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al eliminar departamento: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al eliminar departamento: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool CanExecuteEdit()
        {
            return SelectedDepartment != null;
        }
    }

    public class DepartmentListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int EmployeeCount { get; set; }
    }
}
