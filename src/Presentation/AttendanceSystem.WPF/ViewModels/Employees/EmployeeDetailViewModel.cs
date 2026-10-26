using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using Prism.Navigation.Regions;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    public class EmployeeDetailViewModel : ViewModelBase, INavigationAware
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private bool _isEditMode;
        private string _originalId = string.Empty;

        // Form Properties
        private string _id = string.Empty;
        private string _firstName = string.Empty;
        private string _lastName = string.Empty;
        private string _email = string.Empty;
        private string _phoneNumber = string.Empty;
        private DateTime _hireDate = DateTime.Today;
        private Gender _gender = Gender.Male;
        private EmployeeStatus _status = EmployeeStatus.Alta;

        // Selections
        private string? _selectedDepartmentId;
        private string? _selectedPositionId;
        private string? _selectedBranchId;
        private string? _selectedShiftId; // ScheduleId
        private ShiftType _selectedShiftType = ShiftType.Matutino;
        private ShiftType? _originalShiftType;
        private int? _selectedRestDay; // 0=Sunday, etc.

        // Overtime configuration
        private bool _overtimeAuthorized;
        private OvertimeCalculationMethod _overtimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
        private OvertimeCapType _overtimeCapType = OvertimeCapType.None;
        private double? _overtimeCapMinutes;
        private bool _calculateOvertimeBeforeEntry = false;
        private string? _cardNumber;
        private string? _devicePassword;
        private string? _photo;

        // Auditoría
        private DateTime _createdAt;
        private string? _createdBy;
        private DateTime? _updatedAt;
        private string? _updatedBy;

        public DateTime CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }
        public string? CreatedBy { get => _createdBy; set => SetProperty(ref _createdBy, value); }
        public DateTime? UpdatedAt { get => _updatedAt; set => SetProperty(ref _updatedAt, value); }
        public string? UpdatedBy { get => _updatedBy; set => SetProperty(ref _updatedBy, value); }

        public ObservableCollection<EmployeeAuditLogDto> AuditLogs { get; } = new();

        // Validation
        private string _title = "Nuevo Empleado";

        // Catalogs
        public ObservableCollection<DepartmentDto> Departments { get; } = new();
        public ObservableCollection<PositionDto> Positions { get; } = new();
        public ObservableCollection<PositionDto> VisiblePositions { get; } = new(); // Filtered Positions
        public ObservableCollection<BranchDto> Branches { get; } = new();
        public ObservableCollection<ShiftDto> Shifts { get; } = new();
        public ObservableCollection<ShiftDto> VisibleShifts { get; } = new(); // Filtered Shifts

        public IEnumerable<Gender> GenderOptions => Enum.GetValues(typeof(Gender)).Cast<Gender>();
        // Caso 16: vacaciones, incapacidad y baja temporal son períodos con fechas y sólo los registran RH y Administrador.
        public IEnumerable<EmployeeStatus> StatusOptions => Enum.GetValues(typeof(EmployeeStatus)).Cast<EmployeeStatus>()
            .Where(s => CanManageLeave || !AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee.IsLeaveStatus(s) || s == _loadedStatus);
        public IEnumerable<ShiftType> ShiftTypeOptions => Enum.GetValues(typeof(ShiftType)).Cast<ShiftType>();
        public IEnumerable<OvertimeCalculationMethod> OvertimeCalculationMethodOptions => Enum.GetValues(typeof(OvertimeCalculationMethod)).Cast<OvertimeCalculationMethod>();
        public IEnumerable<OvertimeCapType> OvertimeCapTypeOptions => Enum.GetValues(typeof(OvertimeCapType)).Cast<OvertimeCapType>();

        public Dictionary<int, string> DaysOfWeek { get; } = new()
        {
            { 1, "Lunes" }, { 2, "Martes" }, { 3, "Miércoles" }, { 4, "Jueves" }, { 5, "Viernes" }, { 6, "Sábado" }, { 0, "Domingo" }
        };

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (!SetProperty(ref _isEditMode, value)) return;
                // El estatus sólo se cambia al editar: sin avisar, el combo quedaba deshabilitado al abrir un empleado.
                RaisePropertyChanged(nameof(CanChangeStatus));
                RaisePropertyChanged(nameof(StatusChanged));
                RaisePropertyChanged(nameof(StartsLeave));
                RaisePropertyChanged(nameof(StatusDateLabel));
            }
        }

        public string Id
        {
            get => _id;
            set
            {
                if (SetProperty(ref _id, value))
                    ((DelegateCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
        public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
        public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
        public string Email { get => _email; set => SetProperty(ref _email, value); }
        public string PhoneNumber { get => _phoneNumber; set => SetProperty(ref _phoneNumber, value); }
        public DateTime HireDate { get => _hireDate; set => SetProperty(ref _hireDate, value); }
        public Gender Gender { get => _gender; set => SetProperty(ref _gender, value); }
        public EmployeeStatus Status
        {
            get => _status;
            set
            {
                if (!SetProperty(ref _status, value)) return;
                RaisePropertyChanged(nameof(StatusChanged));
                RaisePropertyChanged(nameof(StartsLeave));
                RaisePropertyChanged(nameof(StatusDateLabel));
            }
        }

        private EmployeeStatus _loadedStatus = EmployeeStatus.Alta;
        private DateTime? _statusDate = DateTime.Today;
        private DateTime? _leaveTo;

        public bool CanManageLeave => _authService?.HasPermission(AttendanceSystem.Application.Authorization.Permissions.EmployeesLeave) ?? true;
        public bool CanChangeStatus => IsEditMode && (CanManageLeave || !AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee.IsLeaveStatus(_loadedStatus));
        public bool StatusChanged => IsEditMode && Status != _loadedStatus;
        public bool StartsLeave => StatusChanged && AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee.IsLeaveStatus(Status);
        public string StatusDateLabel => StartsLeave ? "Desde"
            : Status == EmployeeStatus.Alta && AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee.IsLeaveStatus(_loadedStatus) ? "Regresa a trabajar el"
            : Status == EmployeeStatus.Baja ? "Último día laborado" : "Vigente desde";
        public DateTime? StatusDate { get => _statusDate; set => SetProperty(ref _statusDate, value); }
        /// <summary>Último día del período; vacío = hasta regresar a Alta.</summary>
        public DateTime? LeaveTo { get => _leaveTo; set => SetProperty(ref _leaveTo, value); }

        public string? SelectedDepartmentId
        {
            get => _selectedDepartmentId;
            set
            {
                if (SetProperty(ref _selectedDepartmentId, value))
                {
                    FilterPositions();
                }
            }
        }

        public string? SelectedPositionId { get => _selectedPositionId; set => SetProperty(ref _selectedPositionId, value); }
        public string? SelectedBranchId { get => _selectedBranchId; set => SetProperty(ref _selectedBranchId, value); }
        public string? SelectedShiftId { get => _selectedShiftId; set => SetProperty(ref _selectedShiftId, value); }

        public ShiftType SelectedShiftType
        {
            get => _selectedShiftType;
            set
            {
                if (SetProperty(ref _selectedShiftType, value))
                {
                    FilterShifts();
                    // Caso 12: el horario flexible no admite extras antes de la entrada.
                    if (!AllowsOvertimeBeforeEntry) CalculateOvertimeBeforeEntry = false;
                    RaisePropertyChanged(nameof(AllowsOvertimeBeforeEntry));
                }
            }
        }

        public bool AllowsOvertimeBeforeEntry => AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee.AllowsOvertimeBeforeEntry(SelectedShiftType);

        public int? SelectedRestDay { get => _selectedRestDay; set => SetProperty(ref _selectedRestDay, value); }

        private int? _loadedRestDay;
        private List<int>? _loadedRestDays;
        private DevicePrivilege _loadedDevicePrivilege = DevicePrivilege.User;
        public bool OvertimeAuthorized
        {
            get => _overtimeAuthorized;
            set
            {
                if (!SetProperty(ref _overtimeAuthorized, value) || value) return;
                // Caso 12: sin extras autorizadas no se configuran redondeo, topes ni extras antes de la entrada.
                CalculateOvertimeBeforeEntry = false;
                SelectedOvertimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
                SelectedOvertimeCapType = OvertimeCapType.None;
                OvertimeCapMinutes = null;
            }
        }
        public OvertimeCalculationMethod SelectedOvertimeCalculationMethod { get => _overtimeCalculationMethod; set => SetProperty(ref _overtimeCalculationMethod, value); }
        public OvertimeCapType SelectedOvertimeCapType { get => _overtimeCapType; set => SetProperty(ref _overtimeCapType, value); }
        public double? OvertimeCapMinutes { get => _overtimeCapMinutes; set => SetProperty(ref _overtimeCapMinutes, value); }
        public bool CalculateOvertimeBeforeEntry { get => _calculateOvertimeBeforeEntry; set => SetProperty(ref _calculateOvertimeBeforeEntry, value); }
        public string? CardNumber { get => _cardNumber; set => SetProperty(ref _cardNumber, value); }
        public string? DevicePassword { get => _devicePassword; set => SetProperty(ref _devicePassword, value); }
        public string? Photo { get => _photo; set => SetProperty(ref _photo, value); }


        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        private readonly IAuthenticationStateService? _authService;

        public EmployeeDetailViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator,
            IAuthenticationStateService? authService = null)
        {
            _authService = authService;
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            SaveCommand = new DelegateCommand(async () => await ExecuteSaveAsync());
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            var employeeId = navigationContext.Parameters.ContainsKey("EmployeeId")
                ? navigationContext.Parameters.GetValue<string>("EmployeeId")
                : null;
            _ = InitializeAsync(employeeId);
        }

        private async Task InitializeAsync(string? employeeId)
        {
            await LoadCatalogsAsync();
            if (employeeId != null)
            {
                // Antes de cargar: el estatus y sus fechas dependen de que sea una edición.
                _originalId = employeeId;
                IsEditMode = true;
                Title = "Editar Empleado";
                await LoadEmployeeAsync(employeeId);
            }
            else
            {
                ClearForm();
                IsEditMode = false;
                Title = "Nuevo Empleado";
                FilterPositions();
                FilterShifts();
            }
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private async Task LoadCatalogsAsync()
        {
            try
            {
                var deps = await _mediator.Send(new GetDepartmentsQuery());
                if (deps.IsSuccess)
                {
                    Departments.Clear();
                    Departments.AddRange(deps.Value.OrderBy(d => d.Name));
                }

                var positions = await _mediator.Send(new GetPositionsQuery());
                if (positions.IsSuccess)
                {
                    Positions.Clear();
                    Positions.AddRange(positions.Value.OrderBy(p => p.Name));
                }

                var branches = await _mediator.Send(new GetBranchesQuery());
                if (branches.IsSuccess)
                {
                    Branches.Clear();
                    Branches.AddRange(branches.Value.Where(b => !b.IsExternal));
                }

                var shifts = await _mediator.Send(new GetShiftsQuery());
                if (shifts.IsSuccess)
                {
                    Shifts.Clear();
                    Shifts.AddRange(shifts.Value.OrderBy(s => s.StartTime));
                }

                // Initial filter after loading catalogs
                FilterPositions();
                FilterShifts();
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar catálogos: {ex.Message}");
            }
        }

        private void FilterPositions()
        {
            VisiblePositions.Clear();
            if (string.IsNullOrEmpty(SelectedDepartmentId))
            {
                // If no department selected, show all or none? Usually none or all. user said "depend on department".
                // I will show empty if no department selected to force selection.
                return;
            }

            var dept = Departments.FirstOrDefault(d => d.Id.ToString() == SelectedDepartmentId);
            if (dept != null && dept.PositionIds != null)
            {
                var filtered = Positions.Where(p => dept.PositionIds.Contains(p.Id)).OrderBy(p => p.Name).ToList();
                VisiblePositions.AddRange(filtered);
            }
            else
            {
                // Fallback if no ids mapped
                // VisiblePositions.AddRange(Positions);
            }

            // Validate if selected position is still valid
            if (SelectedPositionId != null && !VisiblePositions.Any(p => p.Id.ToString() == SelectedPositionId))
            {
                SelectedPositionId = null;
            }
        }

        private void FilterShifts()
        {
            VisibleShifts.Clear();
            var filtered = Shifts.Where(s => s.ShiftType == SelectedShiftType).OrderBy(s => s.StartTime).ToList();
            VisibleShifts.AddRange(filtered);

            // Validate if selected shift is still valid
            if (SelectedShiftId != null && !VisibleShifts.Any(s => s.Id.ToString() == SelectedShiftId))
            {
                SelectedShiftId = null;
            }
        }

        private async Task LoadEmployeeAsync(string id)
        {
            SetBusy(true, "Cargando empleado...");
            try
            {
                var result = await _mediator.Send(new GetEmployeeByIdQuery(id));
                if (result.IsSuccess && result.Value != null)
                {
                    var emp = result.Value;
                    Id = emp.Id;
                    FirstName = emp.FirstName;
                    LastName = emp.LastName;
                    Email = emp.Email;
                    PhoneNumber = emp.PhoneNumber ?? string.Empty;
                    HireDate = emp.HireDate;
                    Gender = emp.Gender;
                    _loadedStatus = emp.Status;
                    Status = emp.Status;
                    RaisePropertyChanged(nameof(StatusOptions));
                    RaisePropertyChanged(nameof(CanChangeStatus));
                    SelectedDepartmentId = emp.DepartmentId.ToString();
                    // Filter Positions triggers here

                    SelectedPositionId = emp.PositionId.ToString();
                    SelectedBranchId = emp.BranchId.ToString();

                    _originalShiftType = emp.ShiftType;
                    SelectedShiftType = emp.ShiftType ?? ShiftType.Matutino;
                    // Filter Shifts triggers here

                    SelectedShiftId = emp.ScheduleId?.ToString();

                    SelectedRestDay = emp.RestDay;
                    _loadedRestDay = emp.RestDay;
                    _loadedRestDays = emp.RestDays?.ToList();
                    _loadedDevicePrivilege = emp.DevicePrivilege;
                    OvertimeAuthorized = emp.OvertimeAuthorized;
                    SelectedOvertimeCalculationMethod = emp.OvertimeCalculationMethod;
                    SelectedOvertimeCapType = emp.OvertimeCapType;
                    OvertimeCapMinutes = emp.OvertimeCapMinutes;
                    CalculateOvertimeBeforeEntry = emp.CalculateOvertimeBeforeEntry;
                    CardNumber = emp.CardNumber;
                    DevicePassword = emp.DevicePassword;
                    Photo = emp.Photo;

                    CreatedAt = emp.CreatedAt;
                    CreatedBy = emp.CreatedBy;
                    UpdatedAt = emp.UpdatedAt;
                    UpdatedBy = emp.UpdatedBy;

                    AuditLogs.Clear();
                    try
                    {
                        var auditResult = await _mediator.Send(new GetEmployeeAuditLogsQuery(Id));
                        if (auditResult.IsSuccess)
                        {
                            foreach (var log in auditResult.Value)
                            {
                                AuditLogs.Add(log);
                            }
                        }
                    }
                    catch
                    {
                        // Fallback silencioso si falla la bitácora
                    }
                }
                else
                {
                    await _messageService.ShowErrorAsync("No se encontró el empleado.");
                    ExecuteCancel();
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar empleado: {ex.Message}");
            }
            finally { SetBusy(false); }
        }

        private async Task ExecuteSaveAsync()
        {
            if (!ValidateForm()) return;

            SetBusy(true, "Guardando...");
            try
            {
                bool success = false;
                string error = string.Empty;

                if (IsEditMode)
                {
                    var command = new UpdateEmployeeCommand(
                        Id,
                        FirstName,
                        LastName,
                        Email,
                        PhoneNumber,
                        HireDate,
                        Gender,
                        Status,
                        SelectedBranchId!,
                        SelectedDepartmentId!,
                        SelectedPositionId!,
                        SelectedShiftType,
                        SelectedShiftId,
                        SelectedRestDay,
                        OvertimeAuthorized,
                        SelectedOvertimeCalculationMethod,
                        SelectedOvertimeCapType,
                        OvertimeCapMinutes,
                        CalculateOvertimeBeforeEntry,
                        CardNumber,
                        DevicePassword,
                        Photo,
                        // Caso 13: se conservan el privilegio del reloj y los descansos que esta pantalla no edita.
                        _loadedDevicePrivilege,
                        SelectedRestDay == _loadedRestDay ? _loadedRestDays : null,
                        // Caso 16: la fecha del cambio de estado (inicio del período, regreso, baja o reingreso).
                        StatusChanged ? StatusDate : null,
                        StartsLeave ? LeaveTo : null
                    );

                    var result = await _mediator.Send(command);
                    success = result.IsSuccess;
                    error = result.Error;
                }
                else
                {
                    var command = new CreateEmployeeCommand(
                        Id,
                        FirstName,
                        LastName,
                        Email,
                        PhoneNumber,
                        HireDate,
                        Gender,
                        SelectedBranchId!,
                        SelectedDepartmentId!,
                        SelectedPositionId!,
                        SelectedShiftType,
                        SelectedShiftId,
                        SelectedRestDay,
                        OvertimeAuthorized,
                        SelectedOvertimeCalculationMethod,
                        SelectedOvertimeCapType,
                        OvertimeCapMinutes,
                        CalculateOvertimeBeforeEntry,
                        CardNumber,
                        DevicePassword,
                        Photo
                    );

                    var result = await _mediator.Send(command);
                    success = result.IsSuccess;
                    error = result.Error;
                }

                if (success)
                {
                    await _messageService.ShowSuccessAsync("Empleado guardado correctamente.");
                    _navigationService.GoBack();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al guardar: {error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error inesperado: {ex.Message}");
            }
            finally { SetBusy(false); }
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(Id)) { _messageService.ShowErrorAsync("El ID es requerido."); return false; }
            if (string.IsNullOrWhiteSpace(FirstName)) { _messageService.ShowErrorAsync("El nombre es requerido."); return false; }
            if (string.IsNullOrWhiteSpace(LastName)) { _messageService.ShowErrorAsync("El apellido es requerido."); return false; }
            if (string.IsNullOrWhiteSpace(SelectedDepartmentId)) { _messageService.ShowErrorAsync("El departamento es requerido."); return false; }
            if (string.IsNullOrWhiteSpace(SelectedPositionId)) { _messageService.ShowErrorAsync("El puesto es requerido."); return false; }
            if (string.IsNullOrWhiteSpace(SelectedBranchId)) { _messageService.ShowErrorAsync("La sucursal es requerida."); return false; }

            if (!VisiblePositions.Any(p => p.Id.ToString() == SelectedPositionId))
            {
                _messageService.ShowErrorAsync("El puesto seleccionado no pertenece al departamento seleccionado.");
                return false;
            }

            // Caso 10: ningún empleado queda sin turno. El esquema rotativo (su roster) sólo se genera en la versión web.
            if (SelectedShiftType == ShiftType.Rotativo)
            {
                if (_originalShiftType != ShiftType.Rotativo)
                {
                    _messageService.ShowErrorAsync("El turno rotativo se asigna desde la versión web, donde se genera su esquema de turnos.");
                    return false;
                }
                return true;
            }

            if (string.IsNullOrEmpty(SelectedShiftId)) { _messageService.ShowErrorAsync("El horario es requerido."); return false; }

            if (!VisibleShifts.Any(s => s.Id.ToString() == SelectedShiftId))
            {
                _messageService.ShowErrorAsync("El horario seleccionado no corresponde al turno seleccionado.");
                return false;
            }

            return true;
        }

        private void ExecuteCancel()
        {
            _navigationService.GoBack();
        }

        /// <summary>Formulario de alta: nada del último empleado abierto (estatus, turno, extras, bitácora) se conserva.</summary>
        private void ClearForm()
        {
            Id = string.Empty;
            _originalShiftType = null;
            FirstName = string.Empty;
            LastName = string.Empty;
            Email = string.Empty;
            PhoneNumber = string.Empty;
            HireDate = DateTime.Today;
            Gender = Gender.Male;
            _loadedStatus = EmployeeStatus.Alta;
            Status = EmployeeStatus.Alta;
            StatusDate = DateTime.Today;
            LeaveTo = null;
            RaisePropertyChanged(nameof(StatusOptions));
            SelectedDepartmentId = null;
            SelectedPositionId = null;
            SelectedBranchId = null;
            SelectedShiftType = ShiftType.Matutino;
            SelectedShiftId = null;
            SelectedRestDay = null;
            _loadedRestDay = null;
            _loadedRestDays = null;
            _loadedDevicePrivilege = DevicePrivilege.User;
            OvertimeAuthorized = false;
            SelectedOvertimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
            SelectedOvertimeCapType = OvertimeCapType.None;
            OvertimeCapMinutes = null;
            CalculateOvertimeBeforeEntry = false;
            CardNumber = null;
            DevicePassword = null;
            Photo = null;
            AuditLogs.Clear();
        }
    }
}
