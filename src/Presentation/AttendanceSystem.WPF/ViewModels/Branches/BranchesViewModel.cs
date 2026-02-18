using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Branches;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.WPF.ViewModels.Branches
{
    public class BranchesViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private ObservableCollection<BranchListItem> _branches = new();
        private ObservableCollection<BranchListItem> _filteredBranches = new();
        private BranchListItem? _selectedBranch;
        private string _searchText = string.Empty;
        private List<BranchDto> _allBranchesData = new();

        public ObservableCollection<BranchListItem> Branches
        {
            get => _filteredBranches;
            set => SetProperty(ref _filteredBranches, value);
        }

        public BranchListItem? SelectedBranch
        {
            get => _selectedBranch;
            set => SetProperty(ref _selectedBranch, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterBranches();
                }
            }
        }

        public ICommand AddBranchCommand { get; }
        public ICommand EditBranchCommand { get; }
        public ICommand DeleteBranchCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public BranchesViewModel(
            IFrameNavigationService navigationService,
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            AddBranchCommand = new DelegateCommand(ExecuteAddBranch);
            EditBranchCommand = new DelegateCommand(ExecuteEditBranch, CanExecuteEdit)
                .ObservesProperty(() => SelectedBranch);
            DeleteBranchCommand = new DelegateCommand(async () => await ExecuteDeleteBranchAsync(), CanExecuteEdit)
                .ObservesProperty(() => SelectedBranch);
            RefreshCommand = new DelegateCommand(async () => await LoadBranchesAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadBranchesAsync();
        }

        private async Task LoadBranchesAsync()
        {
            SetBusy(true, "Cargando sucursales...");
            try
            {
                var result = await _mediator.Send(new GetBranchesQuery());
                
                if (result.IsSuccess && result.Value != null)
                {
                    _allBranchesData = result.Value.ToList();
                    _branches.Clear();
                    
                    foreach (var branch in _allBranchesData)
                    {
                        _branches.Add(new BranchListItem
                        {
                            Id = branch.Id,
                            Name = branch.Name,
                            Address = branch.Address ?? "N/A",
                            Phone = "N/A", // TODO: Include phone in DTO
                            EmployeeCount = 0 // TODO: Get count from query
                        });
                    }
                    
                    FilterBranches();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar sucursales: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar sucursales: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterBranches()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Branches = new ObservableCollection<BranchListItem>(_branches);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _branches.Where(b => 
                    b.Name.ToLower().Contains(searchLower) || 
                    (b.Address?.ToLower().Contains(searchLower) ?? false));
                Branches = new ObservableCollection<BranchListItem>(filtered);
            }
        }

        private void ExecuteAddBranch()
        {
            _messageService.ShowMessageAsync("Agregar Sucursal", "Formulario de creación en desarrollo...");
        }

        private void ExecuteEditBranch()
        {
            if (SelectedBranch == null) return;
            _messageService.ShowMessageAsync("Editar Sucursal", $"Formulario de edición para {SelectedBranch.Name} en desarrollo...");
        }

        private async Task ExecuteDeleteBranchAsync()
        {
            if (SelectedBranch == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar la sucursal {SelectedBranch.Name}?");

            if (!confirmed) return;

            // TODO: Implement DeleteBranchCommand in Application layer first
            await _messageService.ShowMessageAsync("No Implementado", "La funcionalidad de eliminar sucursal aún no está implementada en el backend.");
        }

        private bool CanExecuteEdit()
        {
            return SelectedBranch != null;
        }
    }

    public class BranchListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public int EmployeeCount { get; set; }
    }
}
