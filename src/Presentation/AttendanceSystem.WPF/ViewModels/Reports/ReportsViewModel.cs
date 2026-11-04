using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Reports;
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
        // Una vista previa enorme congela la tabla; el archivo sí lleva todo.
        private const int MaxPreviewRows = 3000;

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
        private DataView? _preview;
        private string _previewSummary = string.Empty;

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

            // Mismas casillas que la versión web.
            foreach (var (kind, label) in new[]
            {
                (CardObservationKinds.Absence, "Falta"),
                (CardObservationKinds.MissingCheckIn, "Sin entrada"),
                (CardObservationKinds.MissingCheckOut, "Sin salida"),
                (CardObservationKinds.EarlyDeparture, "Salida anticipada"),
                (CardObservationKinds.TemporaryExit, "Salida temporal"),
            })
            {
                ObservationOptions.Add(new ObservationOption(kind, label, ClearPreview));
            }

            GenerateReportCommand = new DelegateCommand(async () => await ExecuteGenerateReportAsync());
            PreviewCommand = new DelegateCommand(async () => await ExecutePreviewAsync());
            _ = LoadFiltersAsync();
        }

        public ObservableCollection<ReportOption> Reports { get; } = new();
        public ObservableCollection<string> Formats { get; } = new();
        public ObservableCollection<FilterOption> Branches { get; } = new() { FilterOption.All };
        public ObservableCollection<FilterOption> Departments { get; } = new() { FilterOption.All };
        public ObservableCollection<FilterOption> Employees { get; } = new() { FilterOption.All };
        /// <summary>Qué observaciones se imprimen en las tarjetas.</summary>
        public ObservableCollection<ObservationOption> ObservationOptions { get; } = new();

        public ReportOption SelectedReport
        {
            get => _selectedReport;
            set
            {
                if (value == null || !SetProperty(ref _selectedReport, value)) return;
                UpdateFormats();
                RaisePropertyChanged(nameof(ShowDetailed));
                RaisePropertyChanged(nameof(ShowCardOptions));
                RaisePropertyChanged(nameof(ShowDepartment));
                ClearPreview();
            }
        }

        // Cualquier cambio de filtro invalida la vista previa: lo que muestra debe ser lo que se va a guardar.
        public DateTime StartDate { get => _startDate; set { if (SetProperty(ref _startDate, value)) { RaisePropertyChanged(nameof(ShowDetailed)); ClearPreview(); } } }
        public DateTime EndDate { get => _endDate; set { if (SetProperty(ref _endDate, value)) { RaisePropertyChanged(nameof(ShowDetailed)); ClearPreview(); } } }
        public FilterOption SelectedBranch { get => _selectedBranch; set { if (SetProperty(ref _selectedBranch, value ?? FilterOption.All)) ClearPreview(); } }
        public FilterOption SelectedDepartment { get => _selectedDepartment; set { if (SetProperty(ref _selectedDepartment, value ?? FilterOption.All)) ClearPreview(); } }
        public FilterOption SelectedEmployee { get => _selectedEmployee; set { if (SetProperty(ref _selectedEmployee, value ?? FilterOption.All)) ClearPreview(); } }
        public string SelectedFormat { get => _selectedFormat; set { if (SetProperty(ref _selectedFormat, value)) ClearPreview(); } }
        public bool Detailed { get => _detailed; set { if (SetProperty(ref _detailed, value)) ClearPreview(); } }
        public bool IncludeOvertime { get => _includeOvertime; set { if (SetProperty(ref _includeOvertime, value)) ClearPreview(); } }

        /// <summary>Los reportes por tipo se pueden pedir día por día, salvo cuando el periodo es un solo día.</summary>
        public bool ShowDetailed => SelectedReport.Kind is ReportKind.Absences or ReportKind.Lates or ReportKind.Overtime
            or ReportKind.OvertimeByDepartment or ReportKind.WorkedRestDay && StartDate.Date != EndDate.Date;
        public bool ShowCardOptions => SelectedReport.Kind == ReportKind.Cards;
        public bool ShowDepartment => SelectedReport.Kind != ReportKind.Cards;

        /// <summary>Filas de la vista previa; null mientras no se haya pedido o tras cambiar un filtro.</summary>
        public DataView? Preview { get => _preview; private set { if (SetProperty(ref _preview, value)) RaisePropertyChanged(nameof(HasPreview)); } }
        public bool HasPreview => Preview != null;
        public string PreviewSummary { get => _previewSummary; private set => SetProperty(ref _previewSummary, value); }

        private CardObservationKinds ObservationKinds =>
            ObservationOptions.Where(o => o.IsSelected).Aggregate(CardObservationKinds.None, (acc, o) => acc | o.Kind);

        public ICommand GenerateReportCommand { get; }
        public ICommand PreviewCommand { get; }

        private void ClearPreview()
        {
            Preview = null;
            PreviewSummary = string.Empty;
        }

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

        private async Task<bool> ValidateRangeAsync()
        {
            var rangeError = DateRangePolicy.Validate(StartDate, EndDate, DateRangePolicy.MaxQueryDays);
            if (rangeError == null) return true;
            await _messageService.ShowErrorAsync(rangeError);
            return false;
        }

        private async Task ExecutePreviewAsync()
        {
            if (IsBusy || !await ValidateRangeAsync()) return;

            SetBusy(true, $"Preparando vista previa de \"{SelectedReport.Title}\"...");
            try
            {
                var request = CaptureRequest();
                var data = await Task.Run(() => LoadDataAsync(request));
                if (data == null)
                {
                    ClearPreview();
                    await _messageService.ShowWarningAsync("No hay datos para el periodo y los filtros seleccionados. Revise que la asistencia esté calculada.");
                    return;
                }

                var (table, summary) = await Task.Run(() => BuildPreview(data));
                Preview = table.DefaultView;
                PreviewSummary = summary;
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al preparar la vista previa: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ExecuteGenerateReportAsync()
        {
            if (IsBusy || !await ValidateRangeAsync()) return;

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
                var request = CaptureRequest();
                var bytes = await Task.Run(() => BuildReportAsync(request));
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

        /// <summary>Las opciones actuales, leídas en el hilo de la interfaz antes de consultar.</summary>
        private ReportRequest CaptureRequest()
        {
            var start = StartDate.Date;
            var end = EndDate.Date;
            var specificDate = start == end;
            return new ReportRequest
            {
                Kind = SelectedReport.Kind,
                Start = start,
                End = end,
                BranchId = SelectedBranch.Id,
                EmployeeId = SelectedEmployee.Id,
                DepartmentId = SelectedDepartment.Id,
                DepartmentName = SelectedDepartment.Id == null ? null : SelectedDepartment.Name,
                Format = SelectedFormat,
                Detailed = Detailed && !specificDate,
                SpecificDate = specificDate,
                ShowBranch = Branches.Count > 2,
                IncludeOvertime = IncludeOvertime,
                ObservationKinds = ObservationKinds,
            };
        }

        /// <returns>El archivo, o null si no hay datos.</returns>
        private async Task<byte[]?> BuildReportAsync(ReportRequest request)
        {
            var data = await LoadDataAsync(request);
            if (data == null) return null;

            var start = request.Start;
            var end = request.End;
            if (data.Attendance is { } attendance)
            {
                if (request.Kind == ReportKind.Contpaqi) return _exportService.GenerateContpaqiNominaExport(attendance);
                if (request.Kind == ReportKind.AspelNoi) return _exportService.GenerateAspelNoiExport(attendance);

                var (companyName, logo) = await CompanyAsync();
                if (request.Kind == ReportKind.Cards)
                {
                    return _exportService.GenerateAttendanceCardsPdf(GroupByEmployee(attendance), start, end, companyName, logo,
                        request.IncludeOvertime, request.ObservationKinds);
                }
                return request.Format == "PDF"
                    ? _exportService.GeneratePdf(attendance, start, end, companyName, logo)
                    : _exportService.GenerateExcel(attendance, start, end, companyName, logo, detailed: request.Format == "Excel (detallado)");
            }

            var advanced = data.Advanced!;
            return request.Kind switch
            {
                ReportKind.Absences => _exportService.GenerateAdvancedAbsenceExcel(advanced, start, end, request.Detailed, request.SpecificDate, request.ShowBranch),
                ReportKind.Lates => _exportService.GenerateLateArrivalExcel(advanced, start, end, request.Detailed, request.SpecificDate, request.ShowBranch),
                ReportKind.WorkedRestDay => _exportService.GenerateWorkedRestDayExcel(advanced, start, end, request.Detailed, request.ShowBranch),
                _ => _exportService.GenerateOvertimeExcel(advanced, start, end, request.Detailed, request.SpecificDate, request.ShowBranch,
                    groupByDepartment: request.Kind == ReportKind.OvertimeByDepartment),
            };
        }

        /// <summary>Consulta los datos del reporte; la vista previa y el archivo usan exactamente los mismos.</summary>
        /// <returns>Los datos, o null si no hay.</returns>
        private async Task<ReportData?> LoadDataAsync(ReportRequest request)
        {
            var kind = request.Kind;
            var branchId = request.BranchId is { } branch ? BranchId.From(branch) : null;

            if (kind is ReportKind.General or ReportKind.Cards or ReportKind.Contpaqi or ReportKind.AspelNoi)
            {
                var rows = (await _mediator.Send(new GetAttendanceReportQuery(request.Start, request.End, branchId, request.EmployeeId)))
                    .Where(r => request.DepartmentName == null || kind == ReportKind.Cards || r.DepartmentName == request.DepartmentName)
                    .OrderBy(r => long.TryParse(r.EmployeeId, out var id) ? id : long.MaxValue)
                    .ThenBy(r => r.EmployeeId)
                    .ThenBy(r => r.Date)
                    .ToList();
                return rows.Count == 0 ? null : new ReportData(request, rows, null);
            }

            var reportType = kind switch
            {
                ReportKind.Absences => "Faltas",
                ReportKind.Lates => "Retardos",
                ReportKind.WorkedRestDay => "DescansoTrabajado",
                ReportKind.OvertimeByDepartment => "HorasExtraPorDepartamento",
                _ => "HorasExtra",
            };
            var advanced = (await _mediator.Send(new GetAdvancedAttendanceReportQuery(request.Start, request.End, reportType, branchId,
                request.EmployeeId == null ? null : EmployeeId.From(request.EmployeeId),
                request.DepartmentId is { } dept ? DepartmentId.From(Guid.Parse(dept)) : null))).ToList();
            return advanced.Count == 0 ? null : new ReportData(request, null, advanced);
        }

        private static Dictionary<(string EmployeeId, string EmployeeName), List<AttendanceReportViewDto>> GroupByEmployee(
            IEnumerable<AttendanceReportViewDto> rows) =>
            rows.GroupBy(r => (r.EmployeeId, r.EmployeeName)).ToDictionary(g => g.Key, g => g.ToList());

        /// <summary>Tabla con lo que llevará el archivo, en el mismo orden y con los mismos filtros.</summary>
        private static (DataTable Table, string Summary) BuildPreview(ReportData data)
        {
            var request = data.Request;
            var table = new DataTable();
            int employees;
            if (data.Attendance is { } attendance)
            {
                employees = attendance.Select(r => r.EmployeeId).Distinct().Count();
                if (request.Kind == ReportKind.Cards) FillCardsPreview(table, attendance, request);
                else FillDailyPreview(table, attendance, request.Kind);
            }
            else
            {
                var advanced = data.Advanced!;
                employees = advanced.Select(r => r.EmployeeId).Distinct().Count();
                FillAdvancedPreview(table, advanced, request);
            }

            var total = table.Rows.Count;
            var truncated = total > MaxPreviewRows;
            if (truncated)
            {
                for (var i = total - 1; i >= MaxPreviewRows; i--) table.Rows.RemoveAt(i);
            }

            var summary = $"{employees} empleado(s) · {total} fila(s)";
            if (truncated) summary += $" · se muestran las primeras {MaxPreviewRows}; el archivo incluye todas";
            if (request.Kind == ReportKind.General && request.Format == "Excel (resumen)" && !request.SpecificDate)
                summary += " · detalle por día (el Excel resumido totaliza por empleado)";
            return (table, summary);
        }

        private static void FillDailyPreview(DataTable table, List<AttendanceReportViewDto> rows, ReportKind kind)
        {
            foreach (var name in new[] { "No", "Empleado", "Fecha", "Sucursal", "Departamento", "Horario", "Entrada", "Salida",
                         "Trabajado", "Minutos de retardo", "Extra", "Incidencia" })
                table.Columns.Add(name);

            foreach (var r in rows)
            {
                var schedule = r.ScheduledCheckIn == null && r.ScheduledCheckOut == null
                    ? (r.IsRestDay ? "Descanso" : "--")
                    : $"{FormatTime(r.ScheduledCheckIn)}–{FormatTime(r.ScheduledCheckOut)}";
                table.Rows.Add(r.EmployeeId, r.EmployeeName, r.Date.ToString("ddd dd/MM/yy", AttendanceCardObservations.Culture),
                    r.BranchName, r.DepartmentName, schedule,
                    r.ActualCheckIn?.ToString("HH:mm") ?? "--", r.ActualCheckOut?.ToString("HH:mm") ?? "--",
                    FormatMinutes(r.TotalWorkedMinutes), r.LateMinutes > 0 ? r.LateMinutes.ToString() : "",
                    r.RoundedOvertimeMinutes > 0 ? FormatMinutes(r.RoundedOvertimeMinutes) : "", Incidence(r));
            }

            // Los layouts de nómina no llevan horario.
            if (kind is ReportKind.Contpaqi or ReportKind.AspelNoi) table.Columns.Remove("Horario");
        }

        private static void FillCardsPreview(DataTable table, List<AttendanceReportViewDto> rows, ReportRequest request)
        {
            table.Columns.Add("No");
            table.Columns.Add("Empleado");
            table.Columns.Add("Días");
            table.Columns.Add("Faltas");
            table.Columns.Add("Retardos");
            table.Columns.Add("Trabajado");
            if (request.IncludeOvertime) table.Columns.Add("Extra");
            table.Columns.Add("Observaciones");

            foreach (var (key, days) in GroupByEmployee(rows))
            {
                var values = new List<object>
                {
                    key.EmployeeId, key.EmployeeName, days.Count, days.Count(d => d.IsAbsent), days.Count(d => d.LateMinutes > 0),
                    FormatMinutes(days.Sum(d => d.TotalWorkedMinutes)),
                };
                if (request.IncludeOvertime) values.Add(FormatMinutes(days.Sum(d => d.RoundedOvertimeMinutes)));
                // Las mismas líneas que se imprimen en el recuadro de observaciones de la tarjeta.
                var lines = AttendanceCardObservations.Lines(days, request.ObservationKinds);
                values.Add(string.Join("\n", lines));
                table.Rows.Add(values.ToArray());
            }
        }

        private static void FillAdvancedPreview(DataTable table, List<AdvancedReportSummaryDto> rows, ReportRequest request)
        {
            var metric = request.Kind switch
            {
                ReportKind.Absences => "Faltas",
                ReportKind.Lates => "Retardos",
                ReportKind.WorkedRestDay => "Descansos trabajados",
                _ => "Días con extra",
            };

            table.Columns.Add("No");
            table.Columns.Add("Empleado");
            table.Columns.Add("Departamento");
            if (request.ShowBranch) table.Columns.Add("Sucursal");

            if (!request.Detailed)
            {
                table.Columns.Add("Puesto");
                table.Columns.Add(metric);
                table.Columns.Add("Total");
                foreach (var r in rows)
                {
                    var values = new List<object> { r.EmployeeId, r.EmployeeName, r.DepartmentName };
                    if (request.ShowBranch) values.Add(r.BranchName);
                    values.AddRange(new object[] { r.PositionName, r.Count, r.FormattedTotal });
                    table.Rows.Add(values.ToArray());
                }
                return;
            }

            var isLates = request.Kind == ReportKind.Lates;
            table.Columns.Add("Fecha");
            table.Columns.Add("Turno");
            table.Columns.Add("Entrada");
            table.Columns.Add("Salida");
            table.Columns.Add("Trabajado");
            if (request.Kind != ReportKind.Absences) table.Columns.Add(isLates ? "Minutos de retardo" : "Extra");
            foreach (var r in rows)
            {
                foreach (var d in r.Details.OrderBy(d => d.Date))
                {
                    var values = new List<object> { r.EmployeeId, r.EmployeeName, r.DepartmentName };
                    if (request.ShowBranch) values.Add(r.BranchName);
                    values.AddRange(new object[]
                    {
                        d.Date.ToString("ddd dd/MM/yy", AttendanceCardObservations.Culture), d.ShiftName, d.CheckIn, d.CheckOut, d.WorkedHours,
                    });
                    if (request.Kind != ReportKind.Absences)
                        values.Add(isLates ? d.LateMinutes.ToString("0") : FormatMinutes((int)d.OvertimeMinutes));
                    table.Rows.Add(values.ToArray());
                }
            }
        }

        private static string Incidence(AttendanceReportViewDto r)
        {
            if (r.IsAbsent) return "Falta";
            if (r.WorkedOnRestDay) return "Descanso trabajado";
            if (r.IsRestDay) return "Descanso";
            var items = new List<string>();
            if (r.MissingCheckIn) items.Add("Sin entrada");
            if (r.MissingCheckOut) items.Add("Sin salida");
            if (r.LateMinutes > 0) items.Add("Retardo");
            return string.Join(", ", items);
        }

        private static string FormatTime(TimeSpan? time) => time?.ToString(@"hh\:mm") ?? "--";

        private static string FormatMinutes(int minutes) =>
            minutes <= 0 ? "00:00" : $"{minutes / 60:00}:{minutes % 60:00}";

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

    /// <summary>Las opciones con que se pidió el reporte; fijas mientras se consulta fuera del hilo de la interfaz.</summary>
    internal sealed record ReportRequest
    {
        public ReportKind Kind { get; init; }
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public string? BranchId { get; init; }
        public string? EmployeeId { get; init; }
        public string? DepartmentId { get; init; }
        public string? DepartmentName { get; init; }
        public string Format { get; init; } = string.Empty;
        public bool Detailed { get; init; }
        public bool SpecificDate { get; init; }
        public bool ShowBranch { get; init; }
        public bool IncludeOvertime { get; init; }
        public CardObservationKinds ObservationKinds { get; init; }
    }

    /// <summary>Datos consultados: asistencia diaria (general, tarjetas, nómina) o resumen por tipo.</summary>
    internal sealed record ReportData(ReportRequest Request, List<AttendanceReportViewDto>? Attendance, List<AdvancedReportSummaryDto>? Advanced);

    /// <summary>Una casilla de observación de las tarjetas.</summary>
    public sealed class ObservationOption : BindableBase
    {
        private readonly Action _changed;
        private bool _isSelected = true;

        public ObservationOption(CardObservationKinds kind, string label, Action changed)
        {
            Kind = kind;
            Label = label;
            _changed = changed;
        }

        public CardObservationKinds Kind { get; }
        public string Label { get; }
        public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) _changed(); } }
    }

    /// <summary>Opción de un filtro; Id nulo = todos.</summary>
    public sealed record FilterOption(string? Id, string Name)
    {
        public static readonly FilterOption All = new(null, "Todos");
    }
}
