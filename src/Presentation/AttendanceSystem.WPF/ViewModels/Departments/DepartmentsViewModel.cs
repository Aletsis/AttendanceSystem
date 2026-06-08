using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Departments.Commands.DeleteDepartment;
using AttendanceSystem.Application.Features.Departments.Commands.CreateDepartment;
using AttendanceSystem.Application.Features.Departments.Commands.UpdateDepartment;
using AttendanceSystem.Application.Features.Departments.Commands.UpdateDepartment;
using AttendanceSystem.Application.Features.Departments;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Abstractions;
using Microsoft.Win32;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Prism.Services.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Departments
{
    public class DepartmentsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IImportService _importService;
        private readonly IReportExportService _exportService;
        private readonly IDialogService _dialogService;

        private ObservableCollection<DepartmentListItem> _departments = new();
        private ObservableCollection<DepartmentListItem> _filteredDepartments = new();
        private DepartmentListItem? _selectedDepartment;
        private string _searchText = string.Empty;
        private List<DepartmentDto> _allDepartmentsData = new();

        public ObservableCollection<DepartmentListItem> Departments
        {
            get => _filteredDepartments;
            set => SetProperty(ref _filteredDepartments, value);
        }

        public DepartmentListItem? SelectedDepartment
        {
            get => _selectedDepartment;
            set => SetProperty(ref _selectedDepartment, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterDepartments();
                }
            }
        }

        public ICommand AddDepartmentCommand { get; }
        public ICommand EditDepartmentCommand { get; }
        public ICommand DeleteDepartmentCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackToDashboardCommand { get; }
        public ICommand ImportDepartmentsCommand { get; }
        public ICommand ExportDepartmentsCommand { get; }
        public ICommand DownloadTemplateCommand { get; }

        public DepartmentsViewModel(
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

            AddDepartmentCommand = new DelegateCommand(ExecuteAddDepartment);
            EditDepartmentCommand = new DelegateCommand(ExecuteEditDepartment, CanExecuteEdit)
                .ObservesProperty(() => SelectedDepartment);
            DeleteDepartmentCommand = new DelegateCommand(async () => await ExecuteDeleteDepartmentAsync(), CanExecuteEdit)
                .ObservesProperty(() => SelectedDepartment);
            RefreshCommand = new DelegateCommand(async () => await LoadDepartmentsAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());
            ImportDepartmentsCommand = new DelegateCommand(async () => await ExecuteImportDepartmentsAsync());
            ExportDepartmentsCommand = new DelegateCommand(async () => await ExecuteExportDepartmentsAsync());
            DownloadTemplateCommand = new DelegateCommand(async () => await ExecuteDownloadTemplateAsync());

            _ = LoadDepartmentsAsync();
        }

        private async Task LoadDepartmentsAsync()
        {
            SetBusy(true, "Cargando departamentos...");
            try
            {
                var result = await _mediator.Send(new GetDepartmentsQuery());
                var employeesResult = await _mediator.Send(new GetAllEmployeesQuery());
                
                if (result.IsSuccess && result.Value != null)
                {
                    var employeeCounts = employeesResult.IsSuccess 
                        ? employeesResult.Value.GroupBy(e => e.DepartmentId).ToDictionary(g => g.Key, g => g.Count())
                        : new Dictionary<Guid, int>();

                    _allDepartmentsData = result.Value.ToList();
                    _departments.Clear();
                    
                    foreach (var dept in _allDepartmentsData)
                    {
                        _departments.Add(new DepartmentListItem
                        {
                            Id = dept.Id,
                            Name = dept.Name,
                            Description = dept.Description,
                            EmployeeCount = employeeCounts.GetValueOrDefault(dept.Id, 0)
                        });
                    }
                    
                    FilterDepartments();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al cargar departamentos: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar departamentos: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void FilterDepartments()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Departments = new ObservableCollection<DepartmentListItem>(_departments);
            }
            else
            {
                var searchLower = SearchText.ToLower();
                var filtered = _departments.Where(d => 
                    d.Name.ToLower().Contains(searchLower) || 
                    (d.Description?.ToLower().Contains(searchLower) ?? false));
                Departments = new ObservableCollection<DepartmentListItem>(filtered);
            }
        }

        private void ExecuteAddDepartment()
        {
            _dialogService.ShowDialog("DepartmentDetailDialog", null, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var name = result.Parameters.GetValue<string>("Name");
                    var description = result.Parameters.GetValue<string>("Description");
                    var positionIds = result.Parameters.GetValue<List<Guid>>("PositionIds");

                    var command = new CreateDepartmentCommand(name, description, positionIds);
                    var createResult = await _mediator.Send(command);

                    if (createResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Departamento creado correctamente.");
                        await LoadDepartmentsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al crear departamento: {createResult.Error}");
                    }
                }
            });
        }

        private void ExecuteEditDepartment()
        {
            if (SelectedDepartment == null) return;

            var deptData = _allDepartmentsData.FirstOrDefault(d => d.Id == SelectedDepartment.Id);
            if (deptData == null) return;

            var parameters = new DialogParameters
            {
                { "DepartmentId", deptData.Id },
                { "Name", deptData.Name },
                { "Description", deptData.Description },
                { "PositionIds", deptData.PositionIds }
            };

            _dialogService.ShowDialog("DepartmentDetailDialog", parameters, async result =>
            {
                if (result.Result == ButtonResult.OK)
                {
                    var name = result.Parameters.GetValue<string>("Name");
                    var description = result.Parameters.GetValue<string>("Description");
                    var positionIds = result.Parameters.GetValue<List<Guid>>("PositionIds");

                    var command = new UpdateDepartmentCommand(deptData.Id, name, description, positionIds);
                    var updateResult = await _mediator.Send(command);

                    if (updateResult.IsSuccess)
                    {
                        await _messageService.ShowSuccessAsync("Departamento actualizado correctamente.");
                        await LoadDepartmentsAsync();
                    }
                    else
                    {
                        await _messageService.ShowErrorAsync($"Error al actualizar departamento: {updateResult.Error}");
                    }
                }
            });
        }

        private async Task ExecuteDeleteDepartmentAsync()
        {
            if (SelectedDepartment == null) return;

            var confirmed = await _messageService.ShowConfirmationAsync(
                "Confirmar eliminación",
                $"¿Está seguro de eliminar el departamento {SelectedDepartment.Name}?");

            if (!confirmed) return;

            SetBusy(true, "Eliminando departamento...");
            try
            {
                var command = new DeleteDepartmentCommand(SelectedDepartment.Id);
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    await _messageService.ShowSuccessAsync("Departamento eliminado correctamente");
                    await LoadDepartmentsAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync($"Error al eliminar departamento: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al eliminar departamento: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool CanExecuteEdit()
        {
            return SelectedDepartment != null;
        }

        private async Task ExecuteImportDepartmentsAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                Title = "Seleccionar archivo de departamentos"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Procesando archivo...");
                try
                {
                    using var stream = File.OpenRead(openFileDialog.FileName);
                    var result = await _importService.ParseDepartmentsAsync(stream);

                    if (result.IsSuccess)
                    {
                        int importedCount = 0;
                        int errorCount = 0;

                        foreach (var item in result.Data)
                        {
                            var command = new CreateDepartmentCommand(item.Name, item.Description);
                            var createResult = await _mediator.Send(command);
                            if (createResult.IsSuccess)
                                importedCount++;
                            else
                                errorCount++;
                        }

                        await _messageService.ShowSuccessAsync($"Importación finalizada. Éxito: {importedCount}, Errores: {errorCount}");
                        await LoadDepartmentsAsync();
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

        private async Task ExecuteExportDepartmentsAsync()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Archivos de Excel (*.xlsx)|*.xlsx",
                FileName = $"Departamentos_{DateTime.Now:yyyyMMdd}.xlsx",
                Title = "Guardar departamentos"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                SetBusy(true, "Generando archivo...");
                try
                {
                    var bytes = _exportService.GenerateDepartmentsExcel(_allDepartmentsData);
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
                FileName = "Plantilla_Departamentos.xlsx",
                Title = "Descargar plantilla de departamentos"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bytes = _importService.GenerateDepartmentsTemplate();
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

    public class DepartmentListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int EmployeeCount { get; set; }
    }
}
