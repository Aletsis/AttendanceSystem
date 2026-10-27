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
        private readonly IAuthenticationStateService _authService;
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

        public List<string> StatusOptions { get; } = new[] { "Todos" }
            .Concat(Enum.GetValues<EmployeeStatus>().Select(s => Converters.EnumDisplay.Text(s)))
            .ToList();

        // Caso 13: sólo se muestran las acciones que el usuario puede hacer (los comandos también lo validan).
        public bool CanCreate => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesCreate);
        public bool CanEdit => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesEdit);
        public bool CanDelete => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesDelete);
        public bool CanImport => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesImport);
        public bool CanExport => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesExport);
        public bool CanSendToDevice => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesSendToDevice);
        public bool CanCreateExternal => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.ExternalEmployeesCreate);
        public bool CanEditExternal => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.ExternalEmployeesEdit);
        public bool CanDeleteExternal => _authService.HasPermission(AttendanceSystem.Application.Authorization.Permissions.ExternalEmployeesDelete);

        public ICommand AddEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand RefreshCommand { get; }
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
            IDialogService dialogService,
            IAuthenticationStateService authService)
        {
            _authService = authService;
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _importService = importService;
            _exportService = exportService;
            _dialogService = dialogService;

            AddEmployeeCommand = new DelegateCommand(ExecuteAddEmployee);
            // Los botones de cada fila mandan su empleado: la acción es sobre esa fila, no sobre la seleccionada.
            EditEmployeeCommand = new DelegateCommand<EmployeeListItem?>(e => ExecuteEditEmployee(e ?? SelectedEmployee));
            DeleteEmployeeCommand = new DelegateCommand<EmployeeListItem?>(async e => await ExecuteDeleteEmployeeAsync(e ?? SelectedEmployee));

            AddExternalEmployeeCommand = new DelegateCommand(ExecuteAddExternalEmployee);
            EditExternalEmployeeCommand = new DelegateCommand<ExternalEmployeeListItem?>(e => ExecuteEditExternalEmployee(e ?? SelectedExternalEmployee));
            DeleteExternalEmployeeCommand = new DelegateCommand<ExternalEmployeeListItem?>(async e => await ExecuteDeleteExternalEmployeeAsync(e ?? SelectedExternalEmployee));

            SwitchToInternalCommand = new DelegateCommand(() => IsExternalMode = false);
            SwitchToExternalCommand = new DelegateCommand(() => IsExternalMode = true);

            RefreshCommand = new DelegateCommand(async () =>
            {
                if (IsExternalMode)
                    await LoadExternalEmployeesAsync();
                else
                    await LoadEmployeesAsync();
            });

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
                            // Antes vacaciones, incapacidad y baja temporal aparecían como "Baja".
                            Status = Converters.EnumDisplay.Text(emp.Status),
                            StatusValue = emp.Status,
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
                            Status = Converters.EnumDisplay.Text(emp.Status),
                            StatusValue = emp.Status,
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

        private void ExecuteEditEmployee(EmployeeListItem? employee)
        {
            if (employee == null) return;

            var parameters = new Prism.Navigation.NavigationParameters();
            parameters.Add("EmployeeId", employee.Id);

            _navigationService.NavigateTo("EmployeeDetailView", parameters);
        }

        private async Task ExecuteDeleteEmployeeAsync(EmployeeListItem? employee)
        {
            if (employee == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar al empleado {employee.EmployeeNumber} - {employee.FullName}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando empleado...");
            try
            {
                var command = new DeleteEmployeeCommand(employee.Id);
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

        private void ExecuteEditExternalEmployee(ExternalEmployeeListItem? employee)
        {
            if (employee == null) return;

            var empData = _allExternalEmployeesData.FirstOrDefault(e => e.Id == employee.Id);
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

        private async Task ExecuteDeleteExternalEmployeeAsync(ExternalEmployeeListItem? employee)
        {
            if (employee == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar al empleado externo {employee.FullName} de la sucursal {employee.BranchName}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando empleado externo...");
            try
            {
                var command = new DeleteExternalEmployeeCommand(employee.Id);
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
                    var shiftsResult = await _mediator.Send(new GetShiftsQuery());

                    if (!branchesResult.IsSuccess || !departmentsResult.IsSuccess || !positionsResult.IsSuccess || !shiftsResult.IsSuccess)
                    {
                        await _messageService.ShowErrorAsync("Error al cargar catálogos para asociar empleados.");
                        return;
                    }

                    // Por nombre sin distinguir mayúsculas; con nombres repetidos se usa el primero (antes la importación fallaba completa).
                    static Dictionary<string, T> ByName<T>(IEnumerable<T> items, Func<T, string> name) =>
                        items.GroupBy(i => name(i).Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
                    static string Key(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

                    var branchMap = ByName(branchesResult.Value.Where(b => !b.IsExternal), b => b.Name);
                    var deptMap = ByName(departmentsResult.Value, d => d.Name);
                    var posMap = ByName(positionsResult.Value, p => p.Name);
                    // Caso 10: cada empleado entra con el horario indicado; su turno es el del horario.
                    var shiftMap = ByName(shiftsResult.Value.Where(s => s.ShiftType != ShiftType.Rotativo), s => s.Name);

                    using var stream = File.OpenRead(openFileDialog.FileName);
                    var result = await _importService.ParseEmployeesAsync(stream);

                    if (result.IsSuccess)
                    {
                        var summary = new ImportSummary();

                        foreach (var item in result.Data)
                        {
                            var label = $"{item.EmployeeId} {item.FirstName} {item.LastName}".Trim();
                            string? missing =
                                !branchMap.TryGetValue(Key(item.BranchName), out _) ? $"no existe la sucursal \"{item.BranchName}\""
                                : !deptMap.TryGetValue(Key(item.DepartmentName), out _) ? $"no existe el departamento \"{item.DepartmentName}\""
                                : !posMap.TryGetValue(Key(item.PositionName), out _) ? $"no existe el puesto \"{item.PositionName}\""
                                : !shiftMap.TryGetValue(Key(item.ScheduleName), out _) ? $"no existe el horario \"{item.ScheduleName}\""
                                : null;
                            if (missing != null)
                            {
                                summary.Fail(label, missing);
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
                                branchMap[Key(item.BranchName)].Id.ToString(),
                                deptMap[Key(item.DepartmentName)].Id.ToString(),
                                posMap[Key(item.PositionName)].Id.ToString(),
                                shiftMap[Key(item.ScheduleName)].ShiftType,
                                shiftMap[Key(item.ScheduleName)].Id.ToString(),
                                null,
                                false,
                                OvertimeCalculationMethod.NoRounding,
                                OvertimeCapType.None,
                                null,
                                false);

                            var createResult = await _mediator.Send(command);
                            if (createResult.IsSuccess) summary.Success();
                            else summary.Fail(label, createResult.Error);
                        }

                        if (summary.Failed == 0) await _messageService.ShowSuccessAsync(summary.ToMessage());
                        else await _messageService.ShowWarningAsync(summary.ToMessage());
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
        public EmployeeStatus StatusValue { get; set; }
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
        public EmployeeStatus StatusValue { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
