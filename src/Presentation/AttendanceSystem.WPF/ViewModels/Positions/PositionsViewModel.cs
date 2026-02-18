using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Positions.Commands.DeletePosition;
using AttendanceSystem.Application.Features.Positions;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.WPF.ViewModels.Positions
{
    public class PositionsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private ObservableCollection<PositionListItem> _positions = new();
        private ObservableCollection<PositionListItem> _filteredPositions = new();
        private PositionListItem? _selectedPosition;
        private string _searchText = string.Empty;
        private List<PositionDto> _allPositionsData = new();

        public ObservableCollection<PositionListItem> Positions
        {
            get => _filteredPositions;
            set => SetProperty(ref _filteredPositions, value);
        }

        public PositionListItem? SelectedPosition
        {
            get => _selectedPosition;
            set => SetProperty(ref _selectedPosition, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterPositions();
                }
            }
        }

        public ICommand AddPositionCommand { get; }
        public ICommand EditPositionCommand { get; }
        public ICommand DeletePositionCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public PositionsViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            AddPositionCommand = new DelegateCommand(ExecuteAddPosition);
            EditPositionCommand = new DelegateCommand(ExecuteEditPosition, CanExecuteEdit)
                .ObservesProperty(() => SelectedPosition);
            DeletePositionCommand = new DelegateCommand(async () => await ExecuteDeletePositionAsync(), CanExecuteEdit)
                .ObservesProperty(() => SelectedPosition);
            RefreshCommand = new DelegateCommand(async () => await LoadPositionsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadPositionsAsync();
        }

        private async Task LoadPositionsAsync()
        {
            SetBusy(true, "Cargando posiciones...");
            try
            {
                var result = await _mediator.Send(new GetPositionsQuery());
                
                if (result.IsSuccess && result.Value != null)
                {
                    _allPositionsData = result.Value.ToList();
                    _positions.Clear();
                    
                    foreach (var pos in _allPositionsData)
                    {
                        _positions.Add(new PositionListItem
                        {
                            Id = pos.Id,
                            Name = pos.Name,
                            Description = pos.Description,
                            DepartmentName = "N/A", // TODO: Include department in DTO
                            EmployeeCount = 0 // TODO: Get count
                        });
                    }
                    
                    FilterPositions();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar posiciones: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar posiciones: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterPositions()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Positions = new ObservableCollection<PositionListItem>(_positions);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _positions.Where(p => 
                    p.Name.ToLower().Contains(searchLower) || 
                    (p.Description?.ToLower().Contains(searchLower) ?? false));
                Positions = new ObservableCollection<PositionListItem>(filtered);
            }
        }

        private void ExecuteAddPosition()
        {
            _messageService.ShowMessageAsync("Agregar Posición", "Formulario de creación en desarrollo...");
        }

        private void ExecuteEditPosition()
        {
            if (SelectedPosition == null) return;
            _messageService.ShowMessageAsync("Editar Posición", $"Formulario de edición para {SelectedPosition.Name} en desarrollo...");
        }

        private async Task ExecuteDeletePositionAsync()
        {
            if (SelectedPosition == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar la posición {SelectedPosition.Name}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando posición...");
            try
            {
                var command = new DeletePositionCommand(SelectedPosition.Id);
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Posición eliminada correctamente");
                    await LoadPositionsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al eliminar posición: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al eliminar posición: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool CanExecuteEdit()
        {
            return SelectedPosition != null;
        }
    }

    public class PositionListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }
}
