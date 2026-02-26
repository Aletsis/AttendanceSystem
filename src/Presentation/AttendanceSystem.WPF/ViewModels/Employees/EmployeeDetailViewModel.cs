using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using Prism.Regions;
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
        private int? _selectedRestDay; // 0=Sunday, etc.
        
        // Overtime configuration
        private bool _overtimeAuthorized;
        private OvertimeCalculationMethod _overtimeCalculationMethod = OvertimeCalculationMethod.NoRounding;
        private OvertimeCapType _overtimeCapType = OvertimeCapType.None;
        private double? _overtimeCapMinutes;
        private bool _calculateOvertimeBeforeEntry = false;

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
        public IEnumerable<EmployeeStatus> StatusOptions => Enum.GetValues(typeof(EmployeeStatus)).Cast<EmployeeStatus>();
        public IEnumerable<ShiftType> ShiftTypeOptions => Enum.GetValues(typeof(ShiftType)).Cast<ShiftType>();
        public IEnumerable<OvertimeCalculationMethod> OvertimeCalculationMethodOptions => Enum.GetValues(typeof(OvertimeCalculationMethod)).Cast<OvertimeCalculationMethod>();
        public IEnumerable<OvertimeCapType> OvertimeCapTypeOptions => Enum.GetValues(typeof(OvertimeCapType)).Cast<OvertimeCapType>();

        public Dictionary<int, string> DaysOfWeek { get; } = new()
        {
            { 1, "Lunes" }, { 2, "Martes" }, { 3, "Miércoles" }, { 4, "Jueves" }, { 5, "Viernes" }, { 6, "Sábado" }, { 0, "Domingo" }
        };

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public bool IsEditMode { get => _isEditMode; set => SetProperty(ref _isEditMode, value); }

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
        public EmployeeStatus Status { get => _status; set => SetProperty(ref _status, value); }
        
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
                }
            } 
        }
        
        public int? SelectedRestDay { get => _selectedRestDay; set => SetProperty(ref _selectedRestDay, value); }
        public bool OvertimeAuthorized { get => _overtimeAuthorized; set => SetProperty(ref _overtimeAuthorized, value); }
        public OvertimeCalculationMethod SelectedOvertimeCalculationMethod { get => _overtimeCalculationMethod; set => SetProperty(ref _overtimeCalculationMethod, value); }
        public OvertimeCapType SelectedOvertimeCapType { get => _overtimeCapType; set => SetProperty(ref _overtimeCapType, value); }
        public double? OvertimeCapMinutes { get => _overtimeCapMinutes; set => SetProperty(ref _overtimeCapMinutes, value); }
        public bool CalculateOvertimeBeforeEntry { get => _calculateOvertimeBeforeEntry; set => SetProperty(ref _calculateOvertimeBeforeEntry, value); }


        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public EmployeeDetailViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            SaveCommand = new DelegateCommand(async () => await ExecuteSaveAsync());
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _ = LoadCatalogsAsync().ContinueWith(async t => 
            {
                if (navigationContext.Parameters.ContainsKey("EmployeeId"))
                {
                    _originalId = navigationContext.Parameters.GetValue<string>("EmployeeId");
                    await LoadEmployeeAsync(_originalId);
                    IsEditMode = true;
                    Title = "Editar Empleado";
                }
                else
                {
                    ClearForm();
                    IsEditMode = false;
                    Title = "Nuevo Empleado";
                    // Trigger initial filters
                    FilterPositions();
                    FilterShifts();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
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
                    Departments.AddRange(deps.Value);
                }

                var positions = await _mediator.Send(new GetPositionsQuery());
                if (positions.IsSuccess)
                {
                    Positions.Clear();
                    Positions.AddRange(positions.Value);
                }

                var branches = await _mediator.Send(new GetBranchesQuery());
                if (branches.IsSuccess)
                {
                    Branches.Clear();
                    Branches.AddRange(branches.Value);
                }

                var shifts = await _mediator.Send(new GetShiftsQuery());
                if (shifts.IsSuccess)
                {
                    Shifts.Clear();
                    Shifts.AddRange(shifts.Value);
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
                var filtered = Positions.Where(p => dept.PositionIds.Contains(p.Id)).ToList();
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
            var filtered = Shifts.Where(s => s.ShiftType == SelectedShiftType).ToList();
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
                    Status = emp.Status;
                    SelectedDepartmentId = emp.DepartmentId.ToString();
                    // Filter Positions triggers here
                    
                    SelectedPositionId = emp.PositionId.ToString();
                    SelectedBranchId = emp.BranchId.ToString();
                    
                    SelectedShiftType = emp.ShiftType ?? ShiftType.Matutino;
                    // Filter Shifts triggers here
                    
                    SelectedShiftId = emp.ScheduleId?.ToString();
                    
                    SelectedRestDay = emp.RestDay;
                    OvertimeAuthorized = emp.OvertimeAuthorized;
                    SelectedOvertimeCalculationMethod = emp.OvertimeCalculationMethod;
                    SelectedOvertimeCapType = emp.OvertimeCapType;
                    OvertimeCapMinutes = emp.OvertimeCapMinutes;
                    CalculateOvertimeBeforeEntry = emp.CalculateOvertimeBeforeEntry;
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
                        CalculateOvertimeBeforeEntry
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
                        CalculateOvertimeBeforeEntry
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
            
            return true;
        }

        private void ExecuteCancel()
        {
            _navigationService.GoBack();
        }

        private void ClearForm()
        {
            Id = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            Email = string.Empty;
            PhoneNumber = string.Empty;
            HireDate = DateTime.Today;
            SelectedDepartmentId = null;
            SelectedPositionId = null;
            SelectedBranchId = null;
            SelectedShiftId = null;
            SelectedRestDay = null;
            OvertimeAuthorized = false;
            CalculateOvertimeBeforeEntry = false;
        }
    }
}
