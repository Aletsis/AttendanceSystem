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
using AttendanceSystem.Application.Features.Roster.Queries.GetRosterEmployees;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Application.DTOs;
using EmployeeAggregate = AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee;
using Permissions = AttendanceSystem.Application.Authorization.Permissions;

namespace AttendanceSystem.WPF.ViewModels.Employees
{
    public class EmployeeDetailViewModel : ViewModelBase, INavigationAware
    {
        // Pestañas del formulario: al validar se abre la que tiene el dato que falta.
        public const int GeneralTab = 0, OrganizationTab = 1, ScheduleTab = 2, OvertimeTab = 3, DeviceTab = 4;

        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IAuthenticationStateService? _authService;

        private bool _isEditMode;
        private int _selectedTabIndex;

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

        // Valores al abrir: los cambios de sucursal, turno, horario, descansos o estatus llevan fecha de vigencia (como en la web).
        private string? _originalBranchId;
        private ShiftType? _originalShiftType;
        private string? _originalShiftId;
        private HashSet<int> _originalRestDays = new();
        private EmployeeStatus _loadedStatus = EmployeeStatus.Alta;
        private DevicePrivilege _loadedDevicePrivilege = DevicePrivilege.User;

        // Overtime configuration
        private bool _overtimeAuthorized;
        private OvertimeCalculationMethod _overtimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
        private OvertimeCapType _overtimeCapType = OvertimeCapType.None;
        private double? _overtimeCapMinutes;
        private bool _calculateOvertimeBeforeEntry;
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
            .Where(s => CanManageLeave || !EmployeeAggregate.IsLeaveStatus(s) || s == _loadedStatus);
        public IEnumerable<ShiftType> ShiftTypeOptions => Enum.GetValues(typeof(ShiftType)).Cast<ShiftType>();
        public IEnumerable<OvertimeCalculationMethod> OvertimeCalculationMethodOptions => Enum.GetValues(typeof(OvertimeCalculationMethod)).Cast<OvertimeCalculationMethod>();
        public IEnumerable<OvertimeCapType> OvertimeCapTypeOptions => Enum.GetValues(typeof(OvertimeCapType)).Cast<OvertimeCapType>();

        /// <summary>Días de descanso: uno o varios, como en la web.</summary>
        public List<DayToggle> RestDays { get; }
        public string RestDaysSummary
        {
            get
            {
                var selected = RestDays.Where(d => d.IsSelected).Select(d => d.Name).ToList();
                return selected.Count == 0 ? "Sin día de descanso" : $"Descansa: {string.Join(", ", selected)}";
            }
        }

        /// <summary>Esquema rotativo del empleado (sólo cuando el turno es Rotativo).</summary>
        public RotationSchemeEditor Rotation { get; } = new();

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (!SetProperty(ref _isEditMode, value)) return;
                // El estatus sólo se cambia al editar: sin avisar, el combo quedaba deshabilitado al abrir un empleado.
                RaisePropertyChanged(nameof(CanChangeStatus));
                RaisePropertyChanged(nameof(CanEditSchedule));
                RaiseDatedChange();
                RaiseRotation();
            }
        }

        /// <summary>Encabezado: iniciales del nombre (o un guion mientras se captura).</summary>
        public string Initials
        {
            get
            {
                var first = FirstName.Trim();
                var last = LastName.Trim();
                var initials = $"{(first.Length > 0 ? first[0] : ' ')}{(last.Length > 0 ? last[0] : ' ')}".Trim().ToUpperInvariant();
                return initials.Length == 0 ? "—" : initials;
            }
        }

        public string Subtitle => IsEditMode
            ? $"No. {Id} · {(FirstName + " " + LastName).Trim()}"
            : "Capture los datos del colaborador. Los campos marcados con * son obligatorios.";

        // Caso 13 (igual que la web): el alta lleva horario; cambiarlo después es de RH y Administrador, igual que las extras.
        public bool CanEditSchedule => !IsEditMode || (_authService?.HasPermission(Permissions.EmployeesEditSchedule) ?? true);
        public bool CanEditOvertime => _authService?.HasPermission(Permissions.EmployeesEditOvertime) ?? true;
        public bool OvertimeOptionsEnabled => OvertimeAuthorized && CanEditOvertime;
        public bool CanManageRoster => _authService?.HasPermission(Permissions.RosterManage) ?? true;

        public string Id
        {
            get => _id;
            set
            {
                if (SetProperty(ref _id, value))
                    RaisePropertyChanged(nameof(Subtitle));
            }
        }
        public string FirstName
        {
            get => _firstName;
            set
            {
                if (!SetProperty(ref _firstName, value)) return;
                RaisePropertyChanged(nameof(Initials));
                RaisePropertyChanged(nameof(Subtitle));
            }
        }
        public string LastName
        {
            get => _lastName;
            set
            {
                if (!SetProperty(ref _lastName, value)) return;
                RaisePropertyChanged(nameof(Initials));
                RaisePropertyChanged(nameof(Subtitle));
            }
        }
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
                RaiseDatedChange();
            }
        }

        private DateTime? _effectiveFrom = DateTime.Today;
        private DateTime? _leaveTo;

        public bool CanManageLeave => _authService?.HasPermission(Permissions.EmployeesLeave) ?? true;
        public bool CanChangeStatus => IsEditMode && (CanManageLeave || !EmployeeAggregate.IsLeaveStatus(_loadedStatus));
        public bool StatusChanged => IsEditMode && Status != _loadedStatus;
        public bool StartsLeave => StatusChanged && EmployeeAggregate.IsLeaveStatus(Status);
        private bool ReturnsFromLeave => Status == EmployeeStatus.Alta && EmployeeAggregate.IsLeaveStatus(_loadedStatus);

        /// <summary>Caso 16 (como la web): todo cambio de sucursal, turno, horario, descansos o estatus lleva fecha de vigencia.</summary>
        public bool HasDatedChange => IsEditMode &&
            (SelectedBranchId != _originalBranchId || SelectedShiftType != _originalShiftType ||
             (!IsRotating && SelectedShiftId != _originalShiftId) || StatusChanged || !_originalRestDays.SetEquals(SelectedRestDayValues));

        public string EffectiveFromTitle => StatusChanged
            ? (StartsLeave ? $"Período de {Converters.EnumDisplay.Text(Status).ToLowerInvariant()}"
                : ReturnsFromLeave ? "Regreso a trabajar"
                : Status == EmployeeStatus.Baja ? "Fecha de la baja" : "Fecha del reingreso")
            : "Vigencia del cambio de sucursal, turno, horario o descansos";
        public string EffectiveFromLabel => StatusChanged
            ? (StartsLeave ? "Desde" : ReturnsFromLeave ? "Regresa a trabajar el" : Status == EmployeeStatus.Baja ? "Último día laborado" : "Vigente desde")
            : "Vigente desde";
        public DateTime? EffectiveFrom { get => _effectiveFrom; set => SetProperty(ref _effectiveFrom, value); }
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
        public string? SelectedBranchId
        {
            get => _selectedBranchId;
            set
            {
                if (SetProperty(ref _selectedBranchId, value)) RaiseDatedChange();
            }
        }
        public string? SelectedShiftId
        {
            get => _selectedShiftId;
            set
            {
                if (SetProperty(ref _selectedShiftId, value)) RaiseDatedChange();
            }
        }

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
                    if (value == ShiftType.Rotativo) ApplyRotationScheme = true;
                    RaiseRotation();
                    RaiseDatedChange();
                }
            }
        }

        public bool AllowsOvertimeBeforeEntry => EmployeeAggregate.AllowsOvertimeBeforeEntry(SelectedShiftType);

        // Esquema rotativo (como la web): el empleado rotativo sigue su roster; el horario y los descansos salen del esquema.
        private RosterEmployeeDto? _existingRoster;
        private bool _editRotation;
        private bool _applyRotationScheme = true;

        public bool IsRotating => SelectedShiftType == ShiftType.Rotativo;
        public bool IsFixedSchedule => !IsRotating;
        public RosterEmployeeDto? ExistingRoster { get => _existingRoster; private set => SetProperty(ref _existingRoster, value); }
        public bool HasExistingRoster => IsRotating && ExistingRoster is { HasRoster: true };
        public string ExistingRosterPeriod => ExistingRoster == null ? string.Empty
            : $"{ExistingRoster.StartDate:dd/MM/yyyy} al {ExistingRoster.EndDate:dd/MM/yyyy} · {ExistingRoster.TotalAssignments} días · Hoy: {ExistingRoster.TodayStatus ?? "sin turno"}";
        public bool EditRotation
        {
            get => _editRotation;
            set
            {
                if (!SetProperty(ref _editRotation, value)) return;
                ApplyRotationScheme = value || !HasExistingRoster;
                RaiseRotation();
            }
        }
        /// <summary>El editor se muestra al asignar el esquema por primera vez o al pedir modificarlo.</summary>
        public bool ShowRotationEditor => IsRotating && (!HasExistingRoster || EditRotation);
        public bool CanModifyRotation => HasExistingRoster && CanManageRoster && CanEditSchedule;
        public bool ApplyRotationScheme { get => _applyRotationScheme; set => SetProperty(ref _applyRotationScheme, value); }

        public bool OvertimeAuthorized
        {
            get => _overtimeAuthorized;
            set
            {
                if (!SetProperty(ref _overtimeAuthorized, value)) return;
                RaisePropertyChanged(nameof(OvertimeOptionsEnabled));
                if (value) return;
                // Caso 12: sin extras autorizadas no se configuran redondeo, topes ni extras antes de la entrada.
                CalculateOvertimeBeforeEntry = false;
                SelectedOvertimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
                SelectedOvertimeCapType = OvertimeCapType.None;
                OvertimeCapMinutes = null;
            }
        }
        public OvertimeCalculationMethod SelectedOvertimeCalculationMethod { get => _overtimeCalculationMethod; set => SetProperty(ref _overtimeCalculationMethod, value); }
        public OvertimeCapType SelectedOvertimeCapType
        {
            get => _overtimeCapType;
            set
            {
                if (!SetProperty(ref _overtimeCapType, value)) return;
                RaisePropertyChanged(nameof(HasOvertimeCap));
                if (value == OvertimeCapType.None) OvertimeCapMinutes = null;
            }
        }
        public bool HasOvertimeCap => SelectedOvertimeCapType != OvertimeCapType.None;
        public double? OvertimeCapMinutes { get => _overtimeCapMinutes; set => SetProperty(ref _overtimeCapMinutes, value); }
        public bool CalculateOvertimeBeforeEntry { get => _calculateOvertimeBeforeEntry; set => SetProperty(ref _calculateOvertimeBeforeEntry, value); }
        public string? CardNumber { get => _cardNumber; set => SetProperty(ref _cardNumber, value); }
        public string? DevicePassword { get => _devicePassword; set => SetProperty(ref _devicePassword, value); }
        public string? Photo { get => _photo; set => SetProperty(ref _photo, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

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
            RestDays = DayToggle.Week(() =>
            {
                RaisePropertyChanged(nameof(RestDaysSummary));
                RaiseDatedChange();
            });

            SaveCommand = new DelegateCommand(async () => await ExecuteSaveAsync());
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private IEnumerable<int> SelectedRestDayValues => RestDays.Where(d => d.IsSelected).Select(d => d.Value);

        private void SetRestDays(IEnumerable<int> days)
        {
            var set = days.ToHashSet();
            foreach (var day in RestDays) day.IsSelected = set.Contains(day.Value);
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
                    Rotation.InitializeShifts(Shifts.ToList());
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
            // Sin departamento no hay puestos: el puesto depende del departamento.
            if (string.IsNullOrEmpty(SelectedDepartmentId)) return;

            var dept = Departments.FirstOrDefault(d => d.Id.ToString() == SelectedDepartmentId);
            if (dept?.PositionIds != null)
            {
                VisiblePositions.AddRange(Positions.Where(p => dept.PositionIds.Contains(p.Id)).OrderBy(p => p.Name));
            }

            if (SelectedPositionId != null && !VisiblePositions.Any(p => p.Id.ToString() == SelectedPositionId))
            {
                SelectedPositionId = null;
            }
        }

        private void FilterShifts()
        {
            VisibleShifts.Clear();
            VisibleShifts.AddRange(Shifts.Where(s => s.ShiftType == SelectedShiftType).OrderBy(s => s.StartTime));

            if (SelectedShiftId != null && !VisibleShifts.Any(s => s.Id.ToString() == SelectedShiftId) && !IsRotating)
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
                    SelectedPositionId = emp.PositionId.ToString();
                    SelectedBranchId = emp.BranchId.ToString();

                    SelectedShiftType = emp.ShiftType ?? ShiftType.Matutino;
                    SelectedShiftId = emp.ScheduleId?.ToString();

                    var restDays = emp.RestDays is { Count: > 0 } ? emp.RestDays : emp.RestDay is { } single ? new List<int> { single } : new List<int>();
                    SetRestDays(restDays);

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

                    // Lo que se acaba de cargar es el punto de partida: sólo lo que cambie después lleva fecha de vigencia.
                    _originalBranchId = SelectedBranchId;
                    _originalShiftType = emp.ShiftType;
                    _originalShiftId = SelectedShiftId;
                    _originalRestDays = SelectedRestDayValues.ToHashSet();
                    EffectiveFrom = DateTime.Today;
                    LeaveTo = null;
                    RaiseDatedChange();

                    if (IsRotating) await LoadRosterInfoAsync();
                    // Con su esquema vigente no se regenera nada salvo que se pida modificarlo.
                    ApplyRotationScheme = IsRotating && !HasExistingRoster;

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
                        // La bitácora es informativa: si falla, el formulario sigue disponible.
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

        private async Task LoadRosterInfoAsync()
        {
            try
            {
                var rosters = await _mediator.Send(new GetRosterEmployeesQuery(null, Id));
                ExistingRoster = rosters?.FirstOrDefault(r => r.EmployeeId == Id);
                if (ExistingRoster is { HasRoster: true }) Rotation.SelectSchemeFromName(ExistingRoster.SchemeName);
            }
            catch
            {
                // Sin el resumen del roster se ofrece asignar el esquema, como cuando no tiene.
                ExistingRoster = null;
            }
            RaiseRotation();
        }

        private async Task ExecuteSaveAsync()
        {
            if (!ValidateForm(out var error, out var tab))
            {
                SelectedTabIndex = tab;
                await _messageService.ShowErrorAsync(error);
                return;
            }

            SetBusy(true, "Guardando...");
            try
            {
                // Como en la web: el rotativo toma su horario y, en 3x8/2x8 con días fijos, sus descansos del esquema.
                var sendRotation = IsRotating && ShowRotationEditor && ApplyRotationScheme;
                if (sendRotation && IsEditMode && _originalShiftType != ShiftType.Rotativo && HasDatedChange && EffectiveFrom.HasValue)
                {
                    // Quien pasa a ser rotativo sigue su roster desde la vigencia del cambio.
                    Rotation.StartDate = EffectiveFrom.Value.Date;
                }

                var scheduleId = SelectedShiftId;
                var restDays = SelectedRestDayValues.ToList();
                if (IsRotating && sendRotation)
                {
                    if (Rotation.Model.PrimaryShift != Guid.Empty) scheduleId = Rotation.Model.PrimaryShift.ToString();
                    if (Rotation.Model.ProfileRestDays is { } profileRestDays) restDays = profileRestDays.Select(d => (int)d).ToList();
                }
                int? restDay = restDays.Count > 0 ? restDays[0] : null;

                bool success;
                string error2;

                if (IsEditMode)
                {
                    var result = await _mediator.Send(new UpdateEmployeeCommand(
                        Id, FirstName, LastName, Email, PhoneNumber, HireDate, Gender, Status,
                        SelectedBranchId!, SelectedDepartmentId!, SelectedPositionId!,
                        SelectedShiftType, scheduleId, restDay,
                        OvertimeAuthorized, SelectedOvertimeCalculationMethod, SelectedOvertimeCapType, OvertimeCapMinutes, CalculateOvertimeBeforeEntry,
                        CardNumber, DevicePassword, Photo,
                        // Caso 13: se conserva el privilegio del reloj, que esta pantalla no edita.
                        _loadedDevicePrivilege,
                        restDays,
                        // Caso 16: la vigencia del cambio (inicio del período, regreso, baja, reingreso o cambio de horario).
                        HasDatedChange ? EffectiveFrom : null,
                        StartsLeave ? LeaveTo : null));
                    success = result.IsSuccess;
                    error2 = result.Error;
                }
                else
                {
                    var result = await _mediator.Send(new CreateEmployeeCommand(
                        Id, FirstName, LastName, Email, PhoneNumber, HireDate, Gender,
                        SelectedBranchId!, SelectedDepartmentId!, SelectedPositionId!,
                        SelectedShiftType, scheduleId, restDay,
                        OvertimeAuthorized, SelectedOvertimeCalculationMethod, SelectedOvertimeCapType, OvertimeCapMinutes, CalculateOvertimeBeforeEntry,
                        CardNumber, DevicePassword, Photo,
                        DevicePrivilege.User,
                        restDays));
                    success = result.IsSuccess;
                    error2 = result.Error;
                }

                if (!success)
                {
                    await _messageService.ShowErrorAsync($"Error al guardar: {error2}");
                    return;
                }

                if (sendRotation)
                {
                    try
                    {
                        await _mediator.Send(Rotation.Model.ToCommand(new List<string> { Id }));
                    }
                    catch (Exception ex)
                    {
                        await _messageService.ShowWarningAsync($"El empleado se guardó, pero no se pudo generar su esquema rotativo: {ex.Message}");
                        _navigationService.GoBack();
                        return;
                    }
                }

                await _messageService.ShowSuccessAsync(sendRotation ? "Empleado y esquema rotativo guardados." : "Empleado guardado correctamente.");
                _navigationService.GoBack();
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error inesperado: {ex.Message}");
            }
            finally { SetBusy(false); }
        }

        private bool ValidateForm(out string error, out int tab)
        {
            (error, tab) = (string.Empty, GeneralTab);
            if (string.IsNullOrWhiteSpace(Id)) { error = "El número de empleado es obligatorio."; return false; }
            if (string.IsNullOrWhiteSpace(FirstName)) { error = "El nombre es obligatorio."; return false; }
            if (string.IsNullOrWhiteSpace(LastName)) { error = "El apellido es obligatorio."; return false; }
            if (HasDatedChange && !EffectiveFrom.HasValue) { error = "Indique la fecha de vigencia del cambio."; return false; }
            if (StartsLeave && LeaveTo.HasValue && LeaveTo.Value.Date < EffectiveFrom!.Value.Date)
            {
                error = "La fecha final del período no puede ser anterior a la inicial.";
                return false;
            }

            tab = OrganizationTab;
            if (string.IsNullOrWhiteSpace(SelectedBranchId)) { error = "Seleccione la sucursal."; return false; }
            if (string.IsNullOrWhiteSpace(SelectedDepartmentId)) { error = "Seleccione el departamento."; return false; }
            if (string.IsNullOrWhiteSpace(SelectedPositionId)) { error = "Seleccione el puesto."; return false; }
            if (!VisiblePositions.Any(p => p.Id.ToString() == SelectedPositionId))
            {
                error = "El puesto seleccionado no pertenece al departamento seleccionado.";
                return false;
            }

            // Caso 10: ningún empleado queda sin turno.
            tab = ScheduleTab;
            if (IsRotating)
            {
                if (!HasExistingRoster && !ApplyRotationScheme)
                {
                    error = "Un empleado rotativo necesita su esquema: marque la opción para generar la proyección de turnos.";
                    return false;
                }
                if (ShowRotationEditor && ApplyRotationScheme && Rotation.Model.Validate() is { IsValid: false } validation)
                {
                    error = validation.ErrorMessage!;
                    return false;
                }
                return true;
            }

            if (string.IsNullOrEmpty(SelectedShiftId)) { error = "Seleccione el horario del empleado."; return false; }
            if (!VisibleShifts.Any(s => s.Id.ToString() == SelectedShiftId))
            {
                error = "El horario seleccionado no corresponde al turno seleccionado.";
                return false;
            }

            tab = OvertimeTab;
            if (HasOvertimeCap && (OvertimeCapMinutes is null or <= 0))
            {
                error = "Indique los minutos del tope de tiempo extra.";
                return false;
            }

            return true;
        }

        private void ExecuteCancel()
        {
            _navigationService.GoBack();
        }

        private void RaiseDatedChange()
        {
            RaisePropertyChanged(nameof(StatusChanged));
            RaisePropertyChanged(nameof(StartsLeave));
            RaisePropertyChanged(nameof(HasDatedChange));
            RaisePropertyChanged(nameof(EffectiveFromTitle));
            RaisePropertyChanged(nameof(EffectiveFromLabel));
            RaisePropertyChanged(nameof(Subtitle));
        }

        private void RaiseRotation()
        {
            RaisePropertyChanged(nameof(IsRotating));
            RaisePropertyChanged(nameof(IsFixedSchedule));
            RaisePropertyChanged(nameof(HasExistingRoster));
            RaisePropertyChanged(nameof(ExistingRosterPeriod));
            RaisePropertyChanged(nameof(ShowRotationEditor));
            RaisePropertyChanged(nameof(CanModifyRotation));
        }

        /// <summary>Formulario de alta: nada del último empleado abierto (estatus, turno, extras, bitácora) se conserva.</summary>
        private void ClearForm()
        {
            Id = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            Email = string.Empty;
            PhoneNumber = string.Empty;
            HireDate = DateTime.Today;
            Gender = Gender.Male;
            _loadedStatus = EmployeeStatus.Alta;
            Status = EmployeeStatus.Alta;
            EffectiveFrom = DateTime.Today;
            LeaveTo = null;
            RaisePropertyChanged(nameof(StatusOptions));
            SelectedDepartmentId = null;
            SelectedPositionId = null;
            SelectedBranchId = null;
            SelectedShiftType = ShiftType.Matutino;
            SelectedShiftId = null;
            SetRestDays(Array.Empty<int>());
            _originalBranchId = null;
            _originalShiftType = null;
            _originalShiftId = null;
            _originalRestDays = new HashSet<int>();
            _loadedDevicePrivilege = DevicePrivilege.User;
            ExistingRoster = null;
            EditRotation = false;
            ApplyRotationScheme = true;
            OvertimeAuthorized = false;
            SelectedOvertimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
            SelectedOvertimeCapType = OvertimeCapType.None;
            OvertimeCapMinutes = null;
            CalculateOvertimeBeforeEntry = false;
            CardNumber = null;
            DevicePassword = null;
            Photo = null;
            AuditLogs.Clear();
            SelectedTabIndex = GeneralTab;
        }
    }
}
