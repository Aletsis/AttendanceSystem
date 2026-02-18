using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.WPF.ViewModels.Shifts
{
    public class ShiftsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

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
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

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
                
                if (result.IsSuccess && result.Value != null)
                {
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
                            IsNightShift = s.ShiftType == ShiftType.Nocturno,
                            EmployeeCount = 0 // TODO: Get count from query
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
                    s.Name.ToLower().Contains(searchLower));
                Shifts = new ObservableCollection<ShiftListItem>(filtered);
            }
        }

        private void ExecuteAddShift()
        {
            _messageService.ShowMessageAsync("Agregar Turno", "Formulario de creación en desarrollo...");
        }

        private void ExecuteEditShift()
        {
            if (SelectedShift == null) return;
            _messageService.ShowMessageAsync("Editar Turno", $"Formulario de edición para {SelectedShift.Name} en desarrollo...");
        }

        private async Task ExecuteDeleteShiftAsync()
        {
            if (SelectedShift == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
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
        public bool IsNightShift { get; set; }
        public int EmployeeCount { get; set; }
    }
}
