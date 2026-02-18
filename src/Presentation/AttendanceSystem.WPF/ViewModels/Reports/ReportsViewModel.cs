using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;

namespace AttendanceSystem.WPF.ViewModels.Reports
{
    public class ReportsViewModel : ViewModelBase
    {
        private readonly IFrameNavigationService _navigationService;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;

        private string _selectedReportType = "Asistencia General";
        private DateTime _startDate = DateTime.Today.AddDays(-30);
        private DateTime _endDate = DateTime.Today;
        private string _selectedFormat = "PDF";
        private string _selectedEmployee = "Todos";
        private string _selectedDepartment = "Todos";
        private string _selectedBranch = "Todos";

        public ObservableCollection<string> ReportTypes { get; } = new()
        {
            "Asistencia General",
            "Tarjetas de Asistencia",
            "Llegadas Tarde",
            "Ausencias",
            "Horas Extra",
            "Resumen por Departamento",
            "Resumen por Sucursal"
        };

        public ObservableCollection<string> ExportFormats { get; } = new() { "PDF", "Excel", "CSV" };
        public ObservableCollection<string> Employees { get; } = new() { "Todos" };
        public ObservableCollection<string> Departments { get; } = new() { "Todos" };
        public ObservableCollection<string> Branches { get; } = new() { "Todos" };

        public string SelectedReportType { get => _selectedReportType; set => SetProperty(ref _selectedReportType, value); }
        public DateTime StartDate { get => _startDate; set => SetProperty(ref _startDate, value); }
        public DateTime EndDate { get => _endDate; set => SetProperty(ref _endDate, value); }
        public string SelectedFormat { get => _selectedFormat; set => SetProperty(ref _selectedFormat, value); }
        public string SelectedEmployee { get => _selectedEmployee; set => SetProperty(ref _selectedEmployee, value); }
        public string SelectedDepartment { get => _selectedDepartment; set => SetProperty(ref _selectedDepartment, value); }
        public string SelectedBranch { get => _selectedBranch; set => SetProperty(ref _selectedBranch, value); }

        public ICommand GenerateReportCommand { get; }
        public ICommand PreviewReportCommand { get; }
        public ICommand BackToDashboardCommand { get; }

        public ReportsViewModel(
            IFrameNavigationService navigationService, 
            IMessageService messageService,
            IMediator mediator)
        {
            _navigationService = navigationService;
            _messageService = messageService;
            _mediator = mediator;

            GenerateReportCommand = new DelegateCommand(async () => await ExecuteGenerateReportAsync());
            PreviewReportCommand = new DelegateCommand(async () => await ExecutePreviewReportAsync());
            BackToDashboardCommand = new DelegateCommand(() => _navigationService.NavigateTo<Views.Dashboard.DashboardView>());

            _ = LoadFiltersAsync();
        }

        private async Task LoadFiltersAsync()
        {
            SetBusy(true, "Cargando filtros...");
            try
            {
                // Load Employees
                var empResult = await _mediator.Send(new GetAllEmployeesQuery());
                if (empResult.IsSuccess && empResult.Value != null)
                {
                    Employees.Clear();
                    Employees.Add("Todos");
                    foreach (var emp in empResult.Value) Employees.Add($"{emp.Id} - {emp.FullName}");
                    SelectedEmployee = "Todos";
                }

                // Load Departments
                var deptResult = await _mediator.Send(new GetDepartmentsQuery());
                if (deptResult.IsSuccess && deptResult.Value != null)
                {
                    Departments.Clear();
                    Departments.Add("Todos");
                    foreach (var dept in deptResult.Value) Departments.Add(dept.Name);
                    SelectedDepartment = "Todos";
                }

                // Load Branches
                var branchResult = await _mediator.Send(new GetBranchesQuery());
                if (branchResult.IsSuccess && branchResult.Value != null)
                {
                    Branches.Clear();
                    Branches.Add("Todos");
                    foreach (var branch in branchResult.Value) Branches.Add(branch.Name);
                    SelectedBranch = "Todos";
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar filtros: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ExecuteGenerateReportAsync()
        {
            if (!ValidateDates()) return;

            SetBusy(true, $"Generando reporte {SelectedFormat}...");
            try
            {
                await Task.Delay(2000); // Simulate report generation
                // TODO: Implement actual report generation using IReportService
                await _messageService.ShowSuccessAsync($"Simulación: Reporte generado correctamente como {SelectedFormat}");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private async Task ExecutePreviewReportAsync()
        {
            if (!ValidateDates()) return;

            SetBusy(true, "Generando vista previa...");
            try
            {
                await Task.Delay(1500);
                await _messageService.ShowMessageAsync("Vista Previa", "Funcionalidad de vista previa en desarrollo");
            }
            catch (Exception ex) { await _messageService.ShowErrorAsync($"Error: {ex.Message}"); }
            finally { SetBusy(false); }
        }

        private bool ValidateDates()
        {
            if (EndDate < StartDate)
            {
                _ = _messageService.ShowErrorAsync("La fecha fin debe ser posterior a la fecha inicio");
                return false;
            }
            return true;
        }
    }
}
