using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Reports.Queries.GetAdvancedAttendanceReport;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Win32;

namespace AttendanceSystem.WPF.ViewModels.Reports
{
    /// <summary>
    /// Reportes con los mismos datos y formatos que la versión web (antes esta pantalla sólo simulaba la generación).
    /// </summary>
    public class ReportsViewModel : ViewModelBase
    {
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IReportExportService _exportService;
        private readonly IAuthenticationStateService _authService;

        private ReportOption _selectedReport;
        private DateTime _startDate = DateTime.Today.AddDays(-14);
        private DateTime _endDate = DateTime.Today;
        private FilterOption _selectedBranch = FilterOption.All;
        private FilterOption _selectedDepartment = FilterOption.All;
        private FilterOption _selectedEmployee = FilterOption.All;
        private string _selectedFormat = string.Empty;
        private bool _detailed;
        private bool _includeOvertime = true;

        public ReportsViewModel(
            IMessageService messageService,
            IMediator mediator,
            IReportExportService exportService,
            IAuthenticationStateService authService)
        {
            _messageService = messageService;
            _mediator = mediator;
            _exportService = exportService;
            _authService = authService;

            var reports = new List<ReportOption>
            {
                new(ReportKind.General, "Asistencia general", "Entradas, salidas, retardos, faltas y horas extra por empleado y día."),
                new(ReportKind.Cards, "Tarjetas de asistencia", "Una tarjeta por empleado con sus marcajes del periodo (PDF)."),
                new(ReportKind.Absences, "Faltas", "Faltas por empleado; resumido o día por día."),
                new(ReportKind.Lates, "Retardos", "Retardos y minutos de retardo por empleado."),
                new(ReportKind.Overtime, "Horas extra", "Horas extra por empleado."),
                new(ReportKind.OvertimeByDepartment, "Horas extra por departamento", "Horas extra agrupadas por departamento."),
                new(ReportKind.WorkedRestDay, "Descanso trabajado", "Días de descanso en que el empleado trabajó."),
            };
            // Caso 13: los layouts de nómina sólo los exporta RH o Administrador.
            if (_authService.HasPermission(Permissions.ReportsPayrollExport))
            {
                reports.Add(new(ReportKind.Contpaqi, "Nómina CONTPAQi", "Layout de incidencias para importar en CONTPAQi Nóminas (CSV)."));
                reports.Add(new(ReportKind.AspelNoi, "Nómina Aspel NOI", "Layout de incidencias para importar en Aspel NOI (TXT)."));
            }
            foreach (var report in reports) Reports.Add(report);
            _selectedReport = Reports[0];
            UpdateFormats();

            GenerateReportCommand = new DelegateCommand(async () => await ExecuteGenerateReportAsync());
            _ = LoadFiltersAsync();
        }

        public ObservableCollection<ReportOption> Reports { get; } = new();
        public ObservableCollection<string> Formats { get; } = new();
        public ObservableCollection<FilterOption> Branches { get; } = new() { FilterOption.All };
        public ObservableCollection<FilterOption> Departments { get; } = new() { FilterOption.All };
        public ObservableCollection<FilterOption> Employees { get; } = new() { FilterOption.All };

        public ReportOption SelectedReport
        {
            get => _selectedReport;
            set
            {
                if (value == null || !SetProperty(ref _selectedReport, value)) return;
                UpdateFormats();
                RaisePropertyChanged(nameof(ShowDetailed));
                RaisePropertyChanged(nameof(ShowIncludeOvertime));
                RaisePropertyChanged(nameof(ShowDepartment));
            }
        }

        public DateTime StartDate { get => _startDate; set { if (SetProperty(ref _startDate, value)) RaisePropertyChanged(nameof(ShowDetailed)); } }
        public DateTime EndDate { get => _endDate; set { if (SetProperty(ref _endDate, value)) RaisePropertyChanged(nameof(ShowDetailed)); } }
        public FilterOption SelectedBranch { get => _selectedBranch; set => SetProperty(ref _selectedBranch, value ?? FilterOption.All); }
        public FilterOption SelectedDepartment { get => _selectedDepartment; set => SetProperty(ref _selectedDepartment, value ?? FilterOption.All); }
        public FilterOption SelectedEmployee { get => _selectedEmployee; set => SetProperty(ref _selectedEmployee, value ?? FilterOption.All); }
        public string SelectedFormat { get => _selectedFormat; set => SetProperty(ref _selectedFormat, value); }
        public bool Detailed { get => _detailed; set => SetProperty(ref _detailed, value); }
        public bool IncludeOvertime { get => _includeOvertime; set => SetProperty(ref _includeOvertime, value); }

        /// <summary>Los reportes por tipo se pueden pedir día por día, salvo cuando el periodo es un solo día.</summary>
        public bool ShowDetailed => SelectedReport.Kind is ReportKind.Absences or ReportKind.Lates or ReportKind.Overtime
            or ReportKind.OvertimeByDepartment or ReportKind.WorkedRestDay && StartDate.Date != EndDate.Date;
        public bool ShowIncludeOvertime => SelectedReport.Kind == ReportKind.Cards;
        public bool ShowDepartment => SelectedReport.Kind != ReportKind.Cards;

        public ICommand GenerateReportCommand { get; }

        private void UpdateFormats()
        {
            Formats.Clear();
            var formats = SelectedReport.Kind switch
            {
                ReportKind.General => new[] { "Excel (resumen)", "Excel (detallado)", "PDF" },
                ReportKind.Cards => new[] { "PDF" },
                ReportKind.Contpaqi => new[] { "CSV" },
                ReportKind.AspelNoi => new[] { "TXT" },
                _ => new[] { "Excel" },
            };
            foreach (var format in formats) Formats.Add(format);
            SelectedFormat = Formats[0];
        }

        private async Task LoadFiltersAsync()
        {
            SetBusy(true, "Cargando filtros...");
            try
            {
                var branches = await _mediator.Send(new GetBranchesQuery());
                if (branches.IsSuccess)
                    foreach (var b in branches.Value.Where(b => !b.IsExternal).OrderBy(b => b.Name)) Branches.Add(new FilterOption(b.Id.ToString(), b.Name));

                var departments = await _mediator.Send(new GetDepartmentsQuery());
                if (departments.IsSuccess)
                    foreach (var d in departments.Value.OrderBy(d => d.Name)) Departments.Add(new FilterOption(d.Id.ToString(), d.Name));

                var employees = await _mediator.Send(new GetAllEmployeesQuery());
                if (employees.IsSuccess && employees.Value != null)
                    foreach (var e in employees.Value.OrderBy(e => e.FullName)) Employees.Add(new FilterOption(e.Id, $"{e.Id} - {e.FullName}"));
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
            if (IsBusy) return;
            var rangeError = DateRangePolicy.Validate(StartDate, EndDate, DateRangePolicy.MaxQueryDays);
            if (rangeError != null)
            {
                await _messageService.ShowErrorAsync(rangeError);
                return;
            }

            var (extension, filter) = SelectedFormat switch
            {
                "PDF" => (".pdf", "PDF (*.pdf)|*.pdf"),
                "CSV" => (".csv", "CSV (*.csv)|*.csv"),
                "TXT" => (".txt", "Texto (*.txt)|*.txt"),
                _ => (".xlsx", "Excel (*.xlsx)|*.xlsx"),
            };
            var saveDialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = $"{SelectedReport.Title.Replace(' ', '_')}_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}{extension}",
                Title = "Guardar reporte"
            };
            if (saveDialog.ShowDialog() != true) return;

            SetBusy(true, $"Generando \"{SelectedReport.Title}\"...");
            try
            {
                // Fuera del hilo de la interfaz: un PDF o Excel grande congelaba la ventana (y el indicador de carga).
                var bytes = await Task.Run(BuildReportAsync);
                if (bytes == null)
                {
                    await _messageService.ShowWarningAsync("No hay datos para el periodo y los filtros seleccionados. Revise que la asistencia esté calculada.");
                    return;
                }

                await File.WriteAllBytesAsync(saveDialog.FileName, bytes);
                SetBusy(false);
                if (await _messageService.ShowConfirmationAsync("Reporte generado", $"Se guardó el reporte en:\n{saveDialog.FileName}\n\n¿Desea abrirlo?"))
                {
                    Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al generar el reporte: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <returns>El archivo, o null si no hay datos.</returns>
        private async Task<byte[]?> BuildReportAsync()
        {
            var start = StartDate.Date;
            var end = EndDate.Date;
            var branchId = SelectedBranch.Id is { } branch ? BranchId.From(branch) : null;
            var employeeId = SelectedEmployee.Id;
            var departmentName = SelectedDepartment.Id == null ? null : SelectedDepartment.Name;
            var showBranch = Branches.Count > 2;

            switch (SelectedReport.Kind)
            {
                case ReportKind.General:
                case ReportKind.Cards:
                case ReportKind.Contpaqi:
                case ReportKind.AspelNoi:
                {
                    var data = (await _mediator.Send(new GetAttendanceReportQuery(start, end, branchId, employeeId)))
                        .Where(r => departmentName == null || SelectedReport.Kind == ReportKind.Cards || r.DepartmentName == departmentName)
                        .OrderBy(r => long.TryParse(r.EmployeeId, out var id) ? id : long.MaxValue)
                        .ThenBy(r => r.EmployeeId)
                        .ThenBy(r => r.Date)
                        .ToList();
                    if (data.Count == 0) return null;

                    if (SelectedReport.Kind == ReportKind.Contpaqi) return _exportService.GenerateContpaqiNominaExport(data);
                    if (SelectedReport.Kind == ReportKind.AspelNoi) return _exportService.GenerateAspelNoiExport(data);

                    var (companyName, logo) = await CompanyAsync();
                    if (SelectedReport.Kind == ReportKind.Cards)
                    {
                        var grouped = data.GroupBy(r => (r.EmployeeId, r.EmployeeName)).ToDictionary(g => g.Key, g => g.ToList());
                        return _exportService.GenerateAttendanceCardsPdf(grouped, start, end, companyName, logo, IncludeOvertime);
                    }
                    return SelectedFormat == "PDF"
                        ? _exportService.GeneratePdf(data, start, end, companyName, logo)
                        : _exportService.GenerateExcel(data, start, end, companyName, logo, detailed: SelectedFormat == "Excel (detallado)");
                }
                default:
                {
                    var reportType = SelectedReport.Kind switch
                    {
                        ReportKind.Absences => "Faltas",
                        ReportKind.Lates => "Retardos",
                        ReportKind.WorkedRestDay => "DescansoTrabajado",
                        ReportKind.OvertimeByDepartment => "HorasExtraPorDepartamento",
                        _ => "HorasExtra",
                    };
                    var data = (await _mediator.Send(new GetAdvancedAttendanceReportQuery(start, end, reportType, branchId,
                        employeeId == null ? null : EmployeeId.From(employeeId),
                        SelectedDepartment.Id is { } dept ? DepartmentId.From(Guid.Parse(dept)) : null))).ToList();
                    if (data.Count == 0) return null;

                    var specificDate = start == end;
                    var detailed = Detailed && !specificDate;
                    return SelectedReport.Kind switch
                    {
                        ReportKind.Absences => _exportService.GenerateAdvancedAbsenceExcel(data, start, end, detailed, specificDate, showBranch),
                        ReportKind.Lates => _exportService.GenerateLateArrivalExcel(data, start, end, detailed, specificDate, showBranch),
                        ReportKind.WorkedRestDay => _exportService.GenerateWorkedRestDayExcel(data, start, end, detailed, showBranch),
                        _ => _exportService.GenerateOvertimeExcel(data, start, end, detailed, specificDate, showBranch,
                            groupByDepartment: SelectedReport.Kind == ReportKind.OvertimeByDepartment),
                    };
                }
            }
        }

        private async Task<(string Name, byte[]? Logo)> CompanyAsync()
        {
            var config = await _mediator.Send(new GetSystemConfigurationQuery());
            return config.IsSuccess && config.Value != null
                ? (config.Value.CompanyName, config.Value.CompanyLogo)
                : ("Sistema de Asistencia", null);
        }
    }

    public enum ReportKind { General, Cards, Absences, Lates, Overtime, OvertimeByDepartment, WorkedRestDay, Contpaqi, AspelNoi }

    public sealed record ReportOption(ReportKind Kind, string Title, string Description);

    /// <summary>Opción de un filtro; Id nulo = todos.</summary>
    public sealed record FilterOption(string? Id, string Name)
    {
        public static readonly FilterOption All = new(null, "Todos");
    }
}
