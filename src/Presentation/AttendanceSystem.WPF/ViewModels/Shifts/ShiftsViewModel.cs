using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Application.Features.Shifts.Commands.CreateShift;
using AttendanceSystem.Application.Features.Shifts.Commands.UpdateShift;
using AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Shifts
{
    public class ShiftsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;

        private ObservableCollection<ShiftListItem> _shifts = new();
        private ObservableCollection<ShiftListItem> _filteredShifts = new();
        private ShiftListItem? _selectedShift;
        private string _searchText = string.Empty;
        private List<ShiftDto> _allShiftsData = new();

        public ObservableCollection<ShiftListItem> Shifts
        {
            get => _filteredShifts;
            set => SetProperty(ref _filteredShifts, value);
        }

        public ShiftListItem? SelectedShift
        {
            get => _selectedShift;
            set => SetProperty(ref _selectedShift, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterShifts();
                }
            }
        }

        public ICommand AddShiftCommand { get; }
        public ICommand EditShiftCommand { get; }
        public ICommand DeleteShiftCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public ShiftsViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator,
            IDialogService dialogService)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;
            _dialogService = dialogService;

            AddShiftCommand = new DelegateCommand(ExecuteAddShift);
            EditShiftCommand = new DelegateCommand(ExecuteEditShift, CanExecuteEdit)
                .ObservesProperty(() => SelectedShift);
            DeleteShiftCommand = new DelegateCommand(async () => await ExecuteDeleteShiftAsync(), CanExecuteEdit)
                .ObservesProperty(() => SelectedShift);
            RefreshCommand = new DelegateCommand(async () => await LoadShiftsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadShiftsAsync();
        }

        private async Task LoadShiftsAsync()
        {
            SetBusy(true, "Cargando turnos...");
            try
            {
                var result = await _mediator.Send(new GetShiftsQuery());
                var employeesResult = await _mediator.Send(new GetAllEmployeesQuery());
                
                if (result.IsSuccess && result.Value != null)
                {
                    var employeeCounts = employeesResult.IsSuccess 
                        ? employeesResult.Value
                            .Where(e => e.ScheduleId.HasValue)
                            .GroupBy(e => e.ScheduleId.Value)
                            .ToDictionary(g => g.Key, g => g.Count())
                        : new Dictionary<Guid, int>();

                    _allShiftsData = result.Value.ToList();
                    _shifts.Clear();
                    
                    foreach (var s in _allShiftsData)
                    {
                        _shifts.Add(new ShiftListItem
                        {
                            Id = s.Id,
                            Name = s.Name,
                            StartTime = s.StartTime.ToString(@"hh\:mm"),
                            EndTime = s.EndTime.ToString(@"hh\:mm"),
                            WorkingHours = s.WorkHours.ToString(@"hh\:mm"),
                            ToleranceMinutes = s.ToleranceMinutes,
                            ShiftTypeDisplay = s.ShiftType.ToString(),
                            IsNightShift = s.ShiftType == ShiftType.Nocturno,
                            EmployeeCount = employeeCounts.GetValueOrDefault(s.Id, 0)
                        });
                    }
                    
                    FilterShifts();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar turnos: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar turnos: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterShifts()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Shifts = new ObservableCollection<ShiftListItem>(_shifts);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _shifts.Where(s => 
                    s.Name.ToLower().Contains(searchLower) ||
                    s.ShiftTypeDisplay.ToLower().Contains(searchLower));
                Shifts = new ObservableCollection<ShiftListItem>(filtered);
            }
        }

        private void ExecuteAddShift()
        {
            _dialogService.ShowDialog("ShiftDetailDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var name = result.Parameters.GetValue<string>("Name");
                    var start = result.Parameters.GetValue<TimeSpan>("StartTime");
                    var tolerance = result.Parameters.GetValue<int>("ToleranceMinutes");
                    var workHours = result.Parameters.GetValue<TimeSpan>("WorkHours");
                    var type = result.Parameters.GetValue<ShiftType>("ShiftType");
                    var days = result.Parameters.GetValue<List<ShiftDayDto>>("Days");
                    var roundingsEnabled = result.Parameters.GetValue<bool>("RoundingsEnabled");
                    var roundingInterval = result.Parameters.GetValue<int>("RoundingInterval");

                    var command = new CreateShiftCommand(name, start, tolerance, workHours, type, days, roundingsEnabled, roundingInterval);
                    var createResult = await _mediator.Send(command);

                    if (createResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Turno creado correctamente.");
                        await LoadShiftsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al crear turno: {createResult.Error}");
                    }
                }
            });
        }

        private void ExecuteEditShift()
        {
            if (SelectedShift == null) return;

            var shiftData = _allShiftsData.FirstOrDefault(s => s.Id == SelectedShift.Id);
            if (shiftData == null) return;

            var parameters = new DialogParameters
            {
                { "ShiftId", shiftData.Id },
                { "Name", shiftData.Name },
                { "StartTime", shiftData.StartTime },
                { "ToleranceMinutes", shiftData.ToleranceMinutes },
                { "WorkHours", shiftData.WorkHours },
                { "ShiftType", shiftData.ShiftType },
                { "Days", shiftData.Days },
                { "RoundingsEnabled", shiftData.RoundingsEnabled },
                { "RoundingInterval", shiftData.RoundingInterval }
            };

            _dialogService.ShowDialog("ShiftDetailDialog", parameters, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var name = result.Parameters.GetValue<string>("Name");
                    var start = result.Parameters.GetValue<TimeSpan>("StartTime");
                    var tolerance = result.Parameters.GetValue<int>("ToleranceMinutes");
                    var workHours = result.Parameters.GetValue<TimeSpan>("WorkHours");
                    var type = result.Parameters.GetValue<ShiftType>("ShiftType");
                    var days = result.Parameters.GetValue<List<ShiftDayDto>>("Days");
                    var roundingsEnabled = result.Parameters.GetValue<bool>("RoundingsEnabled");
                    var roundingInterval = result.Parameters.GetValue<int>("RoundingInterval");

                    var command = new UpdateShiftCommand(shiftData.Id, name, start, tolerance, workHours, type, days, roundingsEnabled, roundingInterval);
                    var updateResult = await _mediator.Send(command);

                    if (updateResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Turno actualizado correctamente.");
                        await LoadShiftsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al actualizar turno: {updateResult.Error}");
                    }
                }
            });
        }

        private async Task ExecuteDeleteShiftAsync()
        {
            if (SelectedShift == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Eliminar Turno",
                $"¿Está seguro de eliminar el turno {SelectedShift.Name}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando turno...");
            try
            {
                var shiftId = ShiftId.From(SelectedShift.Id);
                var command = new DeleteShiftCommand(shiftId);
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Turno eliminado correctamente");
                    await LoadShiftsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al eliminar turno: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al eliminar turno: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool CanExecuteEdit()
        {
            return SelectedShift != null;
        }
    }

    public class ShiftListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string WorkingHours { get; set; } = string.Empty;
        public int ToleranceMinutes { get; set; }
        public string ShiftTypeDisplay { get; set; } = string.Empty;
        public bool IsNightShift { get; set; }
        public int EmployeeCount { get; set; }
    }
}
