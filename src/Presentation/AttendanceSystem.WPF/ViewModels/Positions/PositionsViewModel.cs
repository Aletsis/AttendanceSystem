using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Positions.Commands.DeletePosition;
using AttendanceSystem.Application.Features.Positions.Commands.CreatePosition;
using AttendanceSystem.Application.Features.Positions.Commands.UpdatePosition;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Positions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Abstractions;
using Microsoft.Win32;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Positions
{
    public class PositionsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IImportService _importService;
        private readonly IReportExportService _exportService;
        private readonly IDialogService _dialogService;

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
        public ICommand ImportPositionsCommand { get; }
        public ICommand ExportPositionsCommand { get; }
        public ICommand DownloadTemplateCommand { get; }

        public PositionsViewModel(
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

            AddPositionCommand = new DelegateCommand(ExecuteAddPosition);
            EditPositionCommand = new DelegateCommand(ExecuteEditPosition, CanExecuteEdit)
                .ObservesProperty(() => SelectedPosition);
            DeletePositionCommand = new DelegateCommand(async () => await ExecuteDeletePositionAsync(), CanExecuteEdit)
                .ObservesProperty(() => SelectedPosition);
            RefreshCommand = new DelegateCommand(async () => await LoadPositionsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());
            ImportPositionsCommand = new DelegateCommand(async () => await ExecuteImportPositionsAsync());
            ExportPositionsCommand = new DelegateCommand(async () => await ExecuteExportPositionsAsync());
            DownloadTemplateCommand = new DelegateCommand(async () => await ExecuteDownloadTemplateAsync());

            _ = LoadPositionsAsync();
        }

        private async Task LoadPositionsAsync()
        {
            SetBusy(true, "Cargando posiciones...");
            try
            {
                var result = await _mediator.Send(new GetPositionsQuery());
                var deptsResult = await _mediator.Send(new GetDepartmentsQuery());
                var employeesResult = await _mediator.Send(new GetAllEmployeesQuery());

                if (result.IsSuccess && result.Value != null)
                {
                    var deptDict = deptsResult.IsSuccess 
                        ? deptsResult.Value.ToDictionary(d => d.Id, d => d.Name)
                        : new Dictionary<Guid, string>();

                    var employeeCounts = employeesResult.IsSuccess 
                        ? employeesResult.Value.GroupBy(e => e.PositionId).ToDictionary(g => g.Key, g => g.Count())
                        : new Dictionary<Guid, int>();

                    _allPositionsData = result.Value.ToList();
                    _positions.Clear();
                    
                    foreach (var pos in _allPositionsData)
                    {
                        // Intentar encontrar el departamento que tiene este puesto
                        var deptName = "N/A";
                        if (deptsResult.IsSuccess)
                        {
                            var dept = deptsResult.Value.FirstOrDefault(d => d.PositionIds?.Contains(pos.Id) == true);
                            if (dept != null) deptName = dept.Name;
                        }

                        _positions.Add(new PositionListItem
                        {
                            Id = pos.Id,
                            Name = pos.Name,
                            Description = pos.Description,
                            BaseSalary = pos.BaseSalary,
                            DepartmentName = deptName,
                            EmployeeCount = employeeCounts.GetValueOrDefault(pos.Id, 0)
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
                    (p.Description?.ToLower().Contains(searchLower) ?? false) ||
                    p.DepartmentName.ToLower().Contains(searchLower));
                Positions = new ObservableCollection<PositionListItem>(filtered);
            }
        }

        private void ExecuteAddPosition()
        {
            _dialogService.ShowDialog("PositionDetailDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var name = result.Parameters.GetValue<string>("Name");
                    var description = result.Parameters.GetValue<string>("Description");
                    var baseSalary = result.Parameters.GetValue<decimal>("BaseSalary");

                    var command = new CreatePositionCommand(name, description, baseSalary, false);
                    var createResult = await _mediator.Send(command);

                    if (createResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Puesto creado correctamente.");
                        await LoadPositionsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al crear puesto: {createResult.Error}");
                    }
                }
            });
        }

        private void ExecuteEditPosition()
        {
            if (SelectedPosition == null) return;

            var posData = _allPositionsData.FirstOrDefault(p => p.Id == SelectedPosition.Id);
            if (posData == null) return;

            var parameters = new DialogParameters
            {
                { "PositionId", posData.Id },
                { "Name", posData.Name },
                { "Description", posData.Description },
                { "BaseSalary", posData.BaseSalary }
            };

            _dialogService.ShowDialog("PositionDetailDialog", parameters, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var name = result.Parameters.GetValue<string>("Name");
                    var description = result.Parameters.GetValue<string>("Description");
                    var baseSalary = result.Parameters.GetValue<decimal>("BaseSalary");

                    var command = new UpdatePositionCommand(posData.Id, name, description, baseSalary, false);
                    var updateResult = await _mediator.Send(command);

                    if (updateResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Puesto actualizado correctamente.");
                        await LoadPositionsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al actualizar puesto: {updateResult.Error}");
                    }
                }
            });
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

        private async Task ExecuteImportPositionsAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                Title = "Seleccionar archivo de puestos"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Procesando archivo...");
                try
                {
                    using var stream = File.OpenRead(openFileDialog.FileName);
                    var result = await _importService.ParsePositionsAsync(stream);

                    if (result.IsSuccess)
                    {
                        int importedCount = 0;
                        int errorCount = 0;

                        foreach (var item in result.Data)
                        {
                            var command = new CreatePositionCommand(item.Name, item.Description, item.BaseSalary, false);
                            var createResult = await _mediator.Send(command);
                            if (createResult.IsSuccess)
                                importedCount++;
                            else
                                errorCount++;
                        }

                        await _messageService.ShowSuccessAsync($"Importación finalizada. Éxito: {importedCount}, Errores: {errorCount}");
                        await LoadPositionsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al leer el archivo: {result.ErrorMessage}");
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

        private async Task ExecuteExportPositionsAsync()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                FileName = $"Puestos_{DateTime.Now:yyyyMMdd}.xlsx",
                Title = "Guardar puestos"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Generando archivo...");
                try
                {
                    var bytes = _exportService.GeneratePositionsExcel(_allPositionsData);
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
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                FileName = "Plantilla_Puestos.xlsx",
                Title = "Descargar plantilla de puestos"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bytes = _importService.GeneratePositionsTemplate();
                    await File.WriteAllBytesAsync(saveFileDialog.FileName, bytes);
                    await _messageService.ShowSuccessAsync("Plantilla descargada correctamente.");
                }
                catch (Exception ex)
                {
                    await _messageService.ShowErrorAsync($"Error al descargar plantilla: {ex.Message}");
                }
            }
        }
    }

    public class PositionListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal BaseSalary { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }
}
