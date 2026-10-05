using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Application.Features.ExternalEmployees;
using AttendanceSystem.Application.Features.ExternalEmployees.Commands;
using AttendanceSystem.Application.Features.ExternalEmployees.Queries;
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
using Prism.Dialogs;

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

        private bool _isExternalMode;
        private ObservableCollection<EmployeeListItem> _employees = new();
        private ObservableCollection<EmployeeListItem> _filteredEmployees = new();
        private EmployeeListItem? _selectedEmployee;

        private ObservableCollection<ExternalEmployeeListItem> _externalEmployees = new();
        private ObservableCollection<ExternalEmployeeListItem> _filteredExternalEmployees = new();
        private ExternalEmployeeListItem? _selectedExternalEmployee;

        private string _searchText = string.Empty;
        private string _selectedStatus = "Todos";
        private List<EmployeeDto> _allEmployeesData = new();
        private List<ExternalEmployeeDto> _allExternalEmployeesData = new();

        public bool IsExternalMode
        {
            get => _isExternalMode;
            set
            {
                if (SetProperty(ref _isExternalMode, value))
                {
                    RaisePropertyChanged(nameof(IsInternalMode));
                    if (_isExternalMode)
                    {
                        _ = LoadExternalEmployeesAsync();
                    }
                    else
                    {
                        _ = LoadEmployeesAsync();
                    }
                }
            }
        }

        public bool IsInternalMode => !_isExternalMode;

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

        public ObservableCollection<ExternalEmployeeListItem> ExternalEmployees
        {
            get => _filteredExternalEmployees;
            set => SetProperty(ref _filteredExternalEmployees, value);
        }

        public ExternalEmployeeListItem? SelectedExternalEmployee
        {
            get => _selectedExternalEmployee;
            set => SetProperty(ref _selectedExternalEmployee, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    if (IsExternalMode)
                        FilterExternalEmployees();
                    else
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
                    if (IsExternalMode)
                        FilterExternalEmployees();
                    else
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

        public ICommand AddExternalEmployeeCommand { get; }
        public ICommand EditExternalEmployeeCommand { get; }
        public ICommand DeleteExternalEmployeeCommand { get; }
        public ICommand SwitchToInternalCommand { get; }
        public ICommand SwitchToExternalCommand { get; }

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

            AddExternalEmployeeCommand = new DelegateCommand(ExecuteAddExternalEmployee);
            EditExternalEmployeeCommand = new DelegateCommand(ExecuteEditExternalEmployee, () => SelectedExternalEmployee != null)
                .ObservesProperty(() => SelectedExternalEmployee);
            DeleteExternalEmployeeCommand = new DelegateCommand(async () => await ExecuteDeleteExternalEmployeeAsync(), () => SelectedExternalEmployee != null)
                .ObservesProperty(() => SelectedExternalEmployee);

            SwitchToInternalCommand = new DelegateCommand(() => IsExternalMode = false);
            SwitchToExternalCommand = new DelegateCommand(() => IsExternalMode = true);

            RefreshCommand = new DelegateCommand(async () =>
            {
                if (IsExternalMode)
                    await LoadExternalEmployeesAsync();
                else
                    await LoadEmployeesAsync();
            });

            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());
            ImportEmployeesCommand = new DelegateCommand(async () => await ExecuteImportEmployeesAsync());
            ExportEmployeesCommand = new DelegateCommand(async () => await ExecuteExportEmployeesAsync());
            DownloadTemplateCommand = new DelegateCommand(async () => await ExecuteDownloadTemplateAsync());
            SendToDeviceCommand = new DelegateCommand<EmployeeListItem>(async (emp) => await ExecuteSendToDeviceAsync(emp));

            _ = LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            SetBusy(true, "Cargando empleados internos...");
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

            if (SelectedStatus != "Todos")
            {
                query = query.Where(e => e.Status == SelectedStatus);
            }

            Employees = new ObservableCollection<EmployeeListItem>(query);
        }

        private async Task LoadExternalEmployeesAsync()
        {
            SetBusy(true, "Cargando empleados externos...");
            try
            {
                var result = await _mediator.Send(new GetExternalEmployeesQuery());
                if (result.IsSuccess && result.Value != null)
                {
                    _allExternalEmployeesData = result.Value.ToList();
                    _externalEmployees.Clear();

                    foreach (var emp in _allExternalEmployeesData)
                    {
                        _externalEmployees.Add(new ExternalEmployeeListItem
                        {
                            Id = emp.Id,
                            BranchId = emp.BranchId,
                            BranchName = emp.BranchName,
                            BranchCode = emp.BranchCode,
                            EmployeeNumber = emp.EmployeeNumber,
                            FullName = emp.FullName,
                            Email = emp.Email ?? "N/A",
                            Phone = emp.PhoneNumber ?? "N/A",
                            Department = emp.Department ?? "N/A",
                            Position = emp.Position ?? "N/A",
                            CardNumber = emp.CardNumber ?? "N/A",
                            Status = emp.Status == EmployeeStatus.Alta ? "Alta" : "Baja",
                            CreatedAt = emp.CreatedAt
                        });
                    }

                    FilterExternalEmployees();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar empleados externos: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar empleados externos: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterExternalEmployees()
        {
            var query = _externalEmployees.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchLower = SearchText.ToLower();
                query = query.Where(e =>
                    e.FullName.ToLower().Contains(searchLower) ||
                    e.EmployeeNumber.ToLower().Contains(searchLower) ||
                    e.BranchName.ToLower().Contains(searchLower) ||
                    e.BranchCode.ToLower().Contains(searchLower) ||
                    e.Department.ToLower().Contains(searchLower) ||
                    e.Position.ToLower().Contains(searchLower));
            }

            if (SelectedStatus != "Todos")
            {
                query = query.Where(e => e.Status.Equals(SelectedStatus, StringComparison.OrdinalIgnoreCase));
            }

            ExternalEmployees = new ObservableCollection<ExternalEmployeeListItem>(query);
        }

        private void ExecuteAddEmployee()
        {
            var parameters = new Prism.Navigation.NavigationParameters();
            _navigationService.NavigateTo("EmployeeDetailView", parameters);
        }

        private void ExecuteEditEmployee()
        {
            if (SelectedEmployee == null) return;

            var parameters = new Prism.Navigation.NavigationParameters();
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

        private void ExecuteAddExternalEmployee()
        {
            _dialogService.ShowDialog("ExternalEmployeeDetailDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var branchId = result.Parameters.GetValue<Guid>("BranchId");
                    var employeeNumber = result.Parameters.GetValue<string>("EmployeeNumber");
                    var firstName = result.Parameters.GetValue<string>("FirstName");
                    var lastName = result.Parameters.GetValue<string>("LastName");
                    var email = result.Parameters.GetValue<string?>("Email");
                    var phoneNumber = result.Parameters.GetValue<string?>("PhoneNumber");
                    var position = result.Parameters.GetValue<string?>("Position");
                    var department = result.Parameters.GetValue<string?>("Department");
                    var status = result.Parameters.GetValue<EmployeeStatus>("Status");
                    var cardNumber = result.Parameters.GetValue<string?>("CardNumber");

                    var command = new CreateExternalEmployeeCommand(
                        branchId.ToString(),
                        employeeNumber,
                        firstName,
                        lastName,
                        email,
                        phoneNumber,
                        position,
                        department,
                        status,
                        cardNumber);

                    var createResult = await _mediator.Send(command);
                    if (createResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Empleado externo registrado correctamente.");
                        await LoadExternalEmployeesAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al registrar empleado externo: {createResult.Error}");
                    }
                }
            });
        }

        private void ExecuteEditExternalEmployee()
        {
            if (SelectedExternalEmployee == null) return;

            var empData = _allExternalEmployeesData.FirstOrDefault(e => e.Id == SelectedExternalEmployee.Id);
            if (empData == null) return;

            var parameters = new DialogParameters
            {
                { "Id", empData.Id },
                { "BranchId", empData.BranchId },
                { "EmployeeNumber", empData.EmployeeNumber },
                { "FirstName", empData.FirstName },
                { "LastName", empData.LastName },
                { "Email", empData.Email! },
                { "PhoneNumber", empData.PhoneNumber! },
                { "Position", empData.Position! },
                { "Department", empData.Department! },
                { "Status", empData.Status },
                { "CardNumber", empData.CardNumber! }
            };

            _dialogService.ShowDialog("ExternalEmployeeDetailDialog", parameters, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var branchId = result.Parameters.GetValue<Guid>("BranchId");
                    var employeeNumber = result.Parameters.GetValue<string>("EmployeeNumber");
                    var firstName = result.Parameters.GetValue<string>("FirstName");
                    var lastName = result.Parameters.GetValue<string>("LastName");
                    var email = result.Parameters.GetValue<string?>("Email");
                    var phoneNumber = result.Parameters.GetValue<string?>("PhoneNumber");
                    var position = result.Parameters.GetValue<string?>("Position");
                    var department = result.Parameters.GetValue<string?>("Department");
                    var status = result.Parameters.GetValue<EmployeeStatus>("Status");
                    var cardNumber = result.Parameters.GetValue<string?>("CardNumber");

                    var command = new UpdateExternalEmployeeCommand(
                        empData.Id,
                        branchId.ToString(),
                        employeeNumber,
                        firstName,
                        lastName,
                        email,
                        phoneNumber,
                        position,
                        department,
                        status,
                        cardNumber);

                    var updateResult = await _mediator.Send(command);
                    if (updateResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Empleado externo actualizado correctamente.");
                        await LoadExternalEmployeesAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al actualizar empleado externo: {updateResult.Error}");
                    }
                }
            });
        }

        private async Task ExecuteDeleteExternalEmployeeAsync()
        {
            if (SelectedExternalEmployee == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar al empleado externo {SelectedExternalEmployee.FullName} de la sucursal {SelectedExternalEmployee.BranchName}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando empleado externo...");
            try
            {
                var command = new DeleteExternalEmployeeCommand(SelectedExternalEmployee.Id);
                var result = await _mediator.Send(command);

                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Empleado externo eliminado correctamente");
                    await LoadExternalEmployeesAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al eliminar empleado externo: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al eliminar empleado externo: {ex.Message}");
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
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                Title = "Seleccionar archivo de empleados"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Importando empleados...");
                try
                {
                    var branchesResult = await _mediator.Send(new GetBranchesQuery());
                    var departmentsResult = await _mediator.Send(new GetDepartmentsQuery());
                    var positionsResult = await _mediator.Send(new GetPositionsQuery());

                    if (!branchesResult.IsSuccess || !departmentsResult.IsSuccess || !positionsResult.IsSuccess)
                    {
                        await _messageService.ShowErrorAsync("Error al cargar catálogos para asociar empleados.");
                        return;
                    }

                    var branchMap = branchesResult.Value.ToDictionary(b => b.Name.Trim().ToLowerInvariant(), b => b.Id);
                    var deptMap = departmentsResult.Value.ToDictionary(d => d.Name.Trim().ToLowerInvariant(), d => d.Id);
                    var posMap = positionsResult.Value.ToDictionary(p => p.Name.Trim().ToLowerInvariant(), p => p.Id);

                    using var stream = File.OpenRead(openFileDialog.FileName);
                    var result = await _importService.ParseEmployeesAsync(stream);

                    if (result.IsSuccess)
                    {
                        int successCount = 0;
                        int failCount = 0;

                        foreach (var item in result.Data)
                        {
                            if (!branchMap.TryGetValue((item.BranchName ?? string.Empty).Trim().ToLowerInvariant(), out var branchId) ||
                                !deptMap.TryGetValue((item.DepartmentName ?? string.Empty).Trim().ToLowerInvariant(), out var deptId) ||
                                !posMap.TryGetValue((item.PositionName ?? string.Empty).Trim().ToLowerInvariant(), out var posId))
                            {
                                failCount++;
                                continue;
                            }

                            var gender = Enum.TryParse<Gender>(item.Gender, true, out var g) ? g : Gender.Male;

                            var command = new CreateEmployeeCommand(
                                item.EmployeeId,
                                item.FirstName,
                                item.LastName,
                                item.Email,
                                null,
                                item.HireDate,
                                gender,
                                branchId.ToString(),
                                deptId.ToString(),
                                posId.ToString(),
                                null,
                                null,
                                null,
                                false,
                                OvertimeCalculationMethod.NoRounding,
                                OvertimeCapType.None,
                                null,
                                false);

                            var createResult = await _mediator.Send(command);
                            if (createResult.IsSuccess) successCount++;
                            else failCount++;
                        }

                        await _messageService.ShowSuccessAsync($"Importación finalizada. Exitosos: {successCount}, Fallidos: {failCount}");
                        await LoadEmployeesAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al leer archivo: {result.ErrorMessage}");
                    }
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

    public class ExternalEmployeeListItem
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string BranchCode { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
