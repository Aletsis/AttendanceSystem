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
using Microsoft.Win32;
using System.IO;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Application.Features.Devices.Queries.GetActiveDevices;
using AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    public class EmployeesViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IImportService _importService;
        private readonly IReportExportService _exportService;
        private readonly IDialogService _dialogService;

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
        public ICommand ImportEmployeesCommand { get; }
        public ICommand ExportEmployeesCommand { get; }
        public ICommand DownloadTemplateCommand { get; }
        public ICommand SendToDeviceCommand { get; }

        public EmployeesViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator,
            IImportService importService,
            IReportExportService exportService,
            IDialogService dialogService)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _importService = importService;
            _exportService = exportService;
            _dialogService = dialogService;

            AddEmployeeCommand = new DelegateCommand(ExecuteAddEmployee);
            EditEmployeeCommand = new DelegateCommand(ExecuteEditEmployee, CanExecuteEditEmployee)
                .ObservesProperty(() => SelectedEmployee);
            DeleteEmployeeCommand = new DelegateCommand(async () => await ExecuteDeleteEmployeeAsync(), CanExecuteEditEmployee)
                .ObservesProperty(() => SelectedEmployee);
            RefreshCommand = new DelegateCommand(async () => await LoadEmployeesAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());
            ImportEmployeesCommand = new DelegateCommand(async () => await ExecuteImportEmployeesAsync());
            ExportEmployeesCommand = new DelegateCommand(async () => await ExecuteExportEmployeesAsync());
            DownloadTemplateCommand = new DelegateCommand(async () => await ExecuteDownloadTemplateAsync());
            SendToDeviceCommand = new DelegateCommand<EmployeeListItem>(async (emp) => await ExecuteSendToDeviceAsync(emp));

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

        private async Task ExecuteImportEmployeesAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                Title = "Seleccionar archivo de empleados"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Procesando importación...");
                try
                {
                    // 1. Cargar catálogos para mapeo
                    var branchesResult = await _mediator.Send(new GetBranchesQuery());
                    var departmentsResult = await _mediator.Send(new GetDepartmentsQuery());
                    var positionsResult = await _mediator.Send(new GetPositionsQuery());
                    var shiftsResult = await _mediator.Send(new GetShiftsQuery());

                    var branches = branchesResult.IsSuccess ? branchesResult.Value.ToDictionary(b => b.Name, b => b.Id, StringComparer.OrdinalIgnoreCase) : new();
                    var departments = departmentsResult.IsSuccess ? departmentsResult.Value.ToDictionary(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase) : new();
                    var positions = positionsResult.IsSuccess ? positionsResult.Value.ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase) : new();

                    using var stream = File.OpenRead(openFileDialog.FileName);
                    var importResult = await _importService.ParseEmployeesAsync(stream);

                    if (importResult.Errors.Any() && !importResult.ValidEntries.Any())
                    {
                        await _messageService.ShowErrorAsync($"Error al leer el archivo: {string.Join("\n", importResult.Errors.Take(5))}");
                        return;
                    }

                    int successCount = 0;
                    int errorCount = 0;

                    foreach (var dto in importResult.ValidEntries)
                    {
                        try
                        {
                            if (!branches.TryGetValue(dto.BranchName, out var branchId) ||
                                !departments.TryGetValue(dto.DepartmentName, out var deptId) ||
                                !positions.TryGetValue(dto.PositionName, out var posId))
                            {
                                errorCount++;
                                continue;
                            }

                            var gender = Gender.Male;
                            if (!string.IsNullOrWhiteSpace(dto.Gender) && dto.Gender.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                                gender = Gender.Female;

                            var command = new CreateEmployeeCommand(
                                dto.EmployeeId, dto.FirstName, dto.LastName, dto.Email, string.Empty, dto.HireDate, gender,
                                branchId.ToString(), deptId.ToString(), posId.ToString(),
                                ShiftType.Matutino, null, null, false, OvertimeCalculationMethod.NoRounding,
                                OvertimeCapType.None, null, false
                            );

                            var result = await _mediator.Send(command);
                            if (result.IsSuccess) successCount++;
                            else errorCount++;
                        }
                        catch
                        {
                            errorCount++;
                        }
                    }

                    await _messageService.ShowSuccessAsync($"Importación completada. Exitosos: {successCount}, Errores: {errorCount}");
                    await LoadEmployeesAsync();
                }
                catch (Exception ex)
                {
                    await _messageService.ShowErrorAsync($"Error durante la importación: {ex.Message}");
                }
                finally
                {
                    SetBusy(false);
                }
            }
        }

        private async Task ExecuteExportEmployeesAsync()
        {
            if (!Employees.Any())
            {
                await _messageService.ShowWarningAsync("No hay datos para exportar.");
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"Empleados_{DateTime.Now:yyyyMMdd}.xlsx",
                Title = "Guardar exportación de empleados"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Generando archivo...");
                try
                {
                    var filteredIds = Employees.Select(e => e.Id).ToHashSet();
                    var filteredEmployees = _allEmployeesData.Where(e => filteredIds.Contains(e.Id)).ToList();
                    var bytes = _exportService.GenerateEmployeesExcel(filteredEmployees);
                    await File.WriteAllBytesAsync(saveFileDialog.FileName, bytes);
                    await _messageService.ShowSuccessAsync("Archivo exportado correctamente.");
                }
                catch (Exception ex)
                {
                    await _messageService.ShowErrorAsync($"Error al exportar: {ex.Message}");
                }
                finally
                {
                    SetBusy(false);
                }
            }
        }

        private async Task ExecuteDownloadTemplateAsync()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "Plantilla_Empleados.xlsx",
                Title = "Descargar plantilla de importación"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bytes = _importService.GenerateEmployeesTemplate();
                    await File.WriteAllBytesAsync(saveFileDialog.FileName, bytes);
                    await _messageService.ShowSuccessAsync("Plantilla descargada correctamente.");
                }
                catch (Exception ex)
                {
                    await _messageService.ShowErrorAsync($"Error al descargar plantilla: {ex.Message}");
                }
            }
        }

        private async Task ExecuteSendToDeviceAsync(EmployeeListItem employee)
        {
            if (employee == null) return;

            _dialogService.ShowDialog("SelectDeviceDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var deviceId = result.Parameters.GetValue<string>("DeviceId");
                    var deviceName = result.Parameters.GetValue<string>("DeviceName");

                    SetBusy(true, $"Enviando a {deviceName}...");
                    try
                    {
                        var cmdResult = await _mediator.Send(new SendEmployeeToDeviceCommand(employee.Id, deviceId));
                        if (cmdResult.IsSuccess)
                        {
                            await _messageService.ShowSuccessAsync($"Empleado sincronizado correctamente en {deviceName}.");
                        }
                        else
                        {
                            await _messageService.ShowErrorAsync($"Error al sincronizar con {deviceName}: {cmdResult.Error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await _messageService.ShowErrorAsync($"Error durante la sincronización: {ex.Message}");
                    }
                    finally
                    {
                        SetBusy(false);
                    }
                }
            });
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
