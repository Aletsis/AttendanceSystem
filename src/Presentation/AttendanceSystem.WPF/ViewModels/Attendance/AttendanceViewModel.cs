using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.Features.Attendance.PeriodClosure;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using Permissions = AttendanceSystem.Application.Authorization.Permissions;

namespace AttendanceSystem.WPF.ViewModels.Attendance
{
    /// <summary>
    /// Asistencia calculada, igual que el reporte de asistencia de la web: horarios, entradas y salidas, retardos,
    /// tiempo extra y, para RH, la clasificación de salidas temporales y la autorización de extras por día.
    /// </summary>
    public class AttendanceViewModel : ViewModelBase
    {
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IAuthenticationStateService _authService;
        private readonly IReportExportService _reportExportService;
        private readonly Prism.Dialogs.IDialogService _dialogService;

        private ObservableCollection<AttendanceRowItem> _rows = new();
        private List<AttendanceRowItem> _allRows = new();
        private AttendanceRowItem? _selectedRow;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;
        private string _searchText = string.Empty;
        private BranchDto? _selectedBranch;
        private RowFilter _selectedFilter;
        private string? _closedPeriodsText;
        private bool _loaded;

        public ObservableCollection<AttendanceRowItem> Rows { get => _rows; private set => SetProperty(ref _rows, value); }
        public AttendanceRowItem? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

        public DateTime StartDate
        {
            get => _startDate;
            set { if (SetProperty(ref _startDate, value) && _loaded) _ = LoadAsync(); }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set { if (SetProperty(ref _endDate, value) && _loaded) _ = LoadAsync(); }
        }

        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        public ObservableCollection<BranchDto> Branches { get; } = new();
        public BranchDto? SelectedBranch
        {
            get => _selectedBranch;
            set { if (SetProperty(ref _selectedBranch, value) && _loaded) _ = LoadAsync(); }
        }

        public enum RowFilter { All, Absences, Lates, Overtime, PendingDecisions, Incomplete }

        public IReadOnlyList<KeyValuePair<RowFilter, string>> Filters { get; } = new List<KeyValuePair<RowFilter, string>>
        {
            new(RowFilter.All, "Todos los días"),
            new(RowFilter.Absences, "Faltas"),
            new(RowFilter.Lates, "Retardos"),
            new(RowFilter.Overtime, "Con tiempo extra"),
            new(RowFilter.PendingDecisions, "Pendientes de RH (salidas o extras)"),
            new(RowFilter.Incomplete, "Marcajes incompletos")
        };

        public RowFilter SelectedFilter
        {
            get => _selectedFilter;
            set { if (SetProperty(ref _selectedFilter, value)) ApplyFilter(); }
        }

        /// <summary>Caso 17: aviso de los periodos cerrados que toca el rango.</summary>
        public string? ClosedPeriodsText { get => _closedPeriodsText; private set => SetProperty(ref _closedPeriodsText, value); }

        // Resumen del rango (sobre lo que se muestra).
        public int DaysCount => Rows.Count;
        public int AbsencesCount => Rows.Count(r => r.Source.IsAbsent);
        public int LatesCount => Rows.Count(r => r.Source.LateMinutes > 0);
        public string OvertimeTotal => AttendanceRowItem.FormatMinutes(Rows.Sum(r => r.Source.RoundedOvertimeMinutes));
        public int PendingCount => Rows.Count(r => r.HasPendingDecision);

        // Caso 13: sólo se muestran las acciones que el usuario puede hacer (los comandos también lo validan).
        public bool CanCalculate => _authService.HasPermission(Permissions.AttendanceCalculate);
        public bool CanManualPunch => _authService.HasPermission(Permissions.AttendanceManualPunch);
        public bool CanAdjust => _authService.HasPermission(Permissions.AttendanceAdjust);

        public ICommand ManualEntryCommand { get; }
        public ICommand CalculateAttendanceCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand RefreshCommand { get; }

        public AttendanceViewModel(
            IMessageService messageService,
            IMediator mediator,
            IReportExportService reportExportService,
            Prism.Dialogs.IDialogService dialogService,
            IAuthenticationStateService authService)
        {
            _authService = authService;
            _messageService = messageService;
            _mediator = mediator;
            _reportExportService = reportExportService;
            _dialogService = dialogService;

            ManualEntryCommand = new DelegateCommand(ExecuteManualEntry);
            CalculateAttendanceCommand = new DelegateCommand(async () => await ExecuteCalculateAttendanceAsync());
            ExportCommand = new DelegateCommand(async () => await ExecuteExportAsync());
            RefreshCommand = new DelegateCommand(async () => await LoadAsync());

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var branches = await _mediator.Send(new GetBranchesQuery());
                if (branches.IsSuccess)
                {
                    Branches.Add(new BranchDto { Name = "Todas las sucursales" });
                    Branches.AddRange(branches.Value.Where(b => !b.IsExternal).OrderBy(b => b.Name));
                    _selectedBranch = Branches[0];
                    RaisePropertyChanged(nameof(SelectedBranch));
                }
            }
            catch
            {
                // Sin sucursales se consulta todo: el filtro es opcional.
            }
            _loaded = true;
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            var rangeError = DateRangePolicy.Validate(StartDate, EndDate, DateRangePolicy.MaxQueryDays);
            if (rangeError != null)
            {
                await _messageService.ShowWarningAsync(rangeError);
                return;
            }

            SetBusy(true, "Cargando asistencia...");
            try
            {
                var branchId = SelectedBranch is { Id: var id } && id != Guid.Empty ? BranchId.From(id) : null;
                var data = await _mediator.Send(new GetAttendanceReportQuery(StartDate, EndDate, branchId));

                _allRows = data
                    .OrderBy(r => long.TryParse(r.EmployeeId, out var n) ? n : long.MaxValue)
                    .ThenBy(r => r.EmployeeId)
                    .ThenBy(r => r.Date)
                    .Select(r => new AttendanceRowItem(r, CanAdjust, ClassifyExitAsync, SetOvertimeAsync))
                    .ToList();

                ClosedPeriodsText = null;
                if (_authService.HasPermission(Permissions.ReportsView))
                {
                    var closed = await _mediator.Send(new GetClosedPeriodsInRangeQuery(StartDate, EndDate));
                    if (closed is { Count: > 0 })
                        ClosedPeriodsText = $"Periodo cerrado: {string.Join(", ", closed.Select(p => p.Describe()))}. Esos días ya no cambian.";
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar la asistencia: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<AttendanceRowItem> rows = _allRows;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim();
                rows = rows.Where(r => r.Source.EmployeeName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                    || r.Source.EmployeeId.Contains(term, StringComparison.CurrentCultureIgnoreCase));
            }

            rows = SelectedFilter switch
            {
                RowFilter.Absences => rows.Where(r => r.Source.IsAbsent),
                RowFilter.Lates => rows.Where(r => r.Source.LateMinutes > 0),
                RowFilter.Overtime => rows.Where(r => r.Source.RawOvertimeMinutes > 0 || r.Source.RoundedOvertimeMinutes > 0),
                RowFilter.PendingDecisions => rows.Where(r => r.HasPendingDecision),
                RowFilter.Incomplete => rows.Where(r => r.Source.MissingCheckIn || r.Source.MissingCheckOut || r.Source.MissingBlock1CheckOut || r.Source.MissingBlock2CheckIn),
                _ => rows
            };

            Rows = new ObservableCollection<AttendanceRowItem>(rows);
            RaisePropertyChanged(nameof(DaysCount));
            RaisePropertyChanged(nameof(AbsencesCount));
            RaisePropertyChanged(nameof(LatesCount));
            RaisePropertyChanged(nameof(OvertimeTotal));
            RaisePropertyChanged(nameof(PendingCount));
        }

        private async Task ClassifyExitAsync(AttendanceRowItem row, TemporaryExitDto exit, TemporaryExitStatus? status)
        {
            await ApplyDecisionAsync(() => _mediator.Send(new ClassifyTemporaryExitCommand(
                row.Source.EmployeeId, DateOnly.FromDateTime(row.Source.Date), exit.ExitRecordId, status,
                status == null ? null : "Clasificado desde la asistencia (escritorio)",
                ExpectedRevision: row.Source.AdjustmentRevision)));
        }

        private async Task SetOvertimeAsync(AttendanceRowItem row, bool authorize, int? minutes)
        {
            var item = row.Source;
            await ApplyDecisionAsync(() => _mediator.Send(new SetDailyOvertimeAuthorizationCommand(
                item.EmployeeId, DateOnly.FromDateTime(item.Date),
                authorize ? true : null, authorize ? item.CalculateOvertimeBeforeEntry : null,
                authorize ? "Extras autorizadas desde la asistencia (escritorio)" : null,
                ExpectedRevision: item.AdjustmentRevision,
                AuthorizedMinutes: minutes, ForRawMinutes: minutes.HasValue ? item.RawOvertimeMinutes : null)));
        }

        /// <summary>
        /// Caso 13 (como la web): si otra persona cambió el día mientras se veía, la decisión se rechaza; se avisa y se
        /// recarga para revisar y volver a aplicar.
        /// </summary>
        private async Task ApplyDecisionAsync(Func<Task<Result<AttendanceDecisionOutcome>>> send)
        {
            SetBusy(true, "Guardando decisión...");
            Result<AttendanceDecisionOutcome> result;
            try
            {
                result = await send();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                await _messageService.ShowErrorAsync($"No se pudo guardar la decisión: {ex.Message}");
                return;
            }
            SetBusy(false);

            if (result.IsSuccess)
            {
                if (result.Value.Consolidated) await _messageService.ShowSuccessAsync(result.Value.UserMessage);
                else await _messageService.ShowWarningAsync(result.Value.UserMessage);
            }
            else if (result.Error.Contains("mientras se editaba", StringComparison.OrdinalIgnoreCase))
                await _messageService.ShowWarningAsync("Este día fue modificado por otra persona; se recargó con los datos actuales. Revise y vuelva a aplicar.");
            else
                await _messageService.ShowErrorAsync(result.Error);

            await LoadAsync();
        }

        private void ExecuteManualEntry()
        {
            _dialogService.ShowDialog("ManualPunchDialog", null, async result =>
            {
                if (result.Result != Prism.Dialogs.ButtonResult.OK) return;
                var checkTime = result.Parameters.GetValue<DateTime>("CheckTime");
                await _messageService.ShowSuccessAsync($"Marcaje registrado: {result.Parameters.GetValue<string>("EmployeeName")}, {checkTime:dd/MM/yyyy HH:mm}.");
                await LoadAsync();
            });
        }

        private async Task ExecuteCalculateAttendanceAsync()
        {
            // F07-02: la misma validación que aplica el servidor, antes de enviar.
            var rangeError = DateRangePolicy.Validate(StartDate, EndDate, DateRangePolicy.MaxRecalculationDays);
            if (rangeError != null)
            {
                await _messageService.ShowErrorAsync(rangeError);
                return;
            }

            SetBusy(true, "Calculando asistencia...");
            try
            {
                var processedCount = await _mediator.Send(new ProcessDailyAttendanceCommand(StartDate, EndDate));
                await _messageService.ShowSuccessAsync($"Cálculo de asistencia completado. Días procesados: {processedCount}");
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al calcular la asistencia: {ex.Message}");
                return;
            }
            finally
            {
                SetBusy(false);
            }
            await LoadAsync();
        }

        private async Task ExecuteExportAsync()
        {
            // Como en la web: se exporta lo que se está viendo (con búsqueda y filtro).
            var data = Rows.Select(r => r.Source).ToList();
            if (data.Count == 0)
            {
                await _messageService.ShowWarningAsync("No hay datos para exportar con el rango y los filtros actuales.");
                return;
            }

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = $"Asistencia_{StartDate:yyyyMMdd}_a_{EndDate:yyyyMMdd}.xlsx",
                Title = "Guardar asistencia"
            };
            if (saveFileDialog.ShowDialog() != true) return;

            SetBusy(true, "Exportando...");
            try
            {
                var configResult = await _mediator.Send(new GetSystemConfigurationQuery());
                var companyName = configResult.IsSuccess && configResult.Value != null ? configResult.Value.CompanyName : "Sistema de Asistencia";
                var companyLogo = configResult.IsSuccess ? configResult.Value?.CompanyLogo : null;

                var excelBytes = await Task.Run(() => _reportExportService.GenerateExcel(data, StartDate, EndDate, companyName, companyLogo, detailed: true));
                await System.IO.File.WriteAllBytesAsync(saveFileDialog.FileName, excelBytes);
                await _messageService.ShowSuccessAsync("Asistencia exportada correctamente.");
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

    /// <summary>Opción de un menú de la fila (clasificar una salida, autorizar extras).</summary>
    public sealed record RowAction(string Header, ICommand Command);

    /// <summary>Salida temporal del día con sus opciones de clasificación (caso 11: cada salida por separado).</summary>
    public sealed class ExitMenu
    {
        public ExitMenu(string label, bool isPending, IReadOnlyList<RowAction> actions)
        {
            Label = label;
            IsPending = isPending;
            Actions = actions;
        }

        public string Label { get; }
        public bool IsPending { get; }
        public IReadOnlyList<RowAction> Actions { get; }
    }

    /// <summary>Ficha de estado del día: texto y tipo de color (danger, warning, info, success).</summary>
    public sealed record StatusBadge(string Text, string Kind);

    /// <summary>Un día de un empleado, listo para mostrar.</summary>
    public sealed class AttendanceRowItem
    {
        public AttendanceRowItem(
            AttendanceReportViewDto source,
            bool canAdjust,
            Func<AttendanceRowItem, TemporaryExitDto, TemporaryExitStatus?, Task> classifyExit,
            Func<AttendanceRowItem, bool, int?, Task> setOvertime)
        {
            Source = source;
            var split = source.ShiftType == ShiftType.Partido && source.ScheduledBlock2CheckIn.HasValue;

            Scheduled = split
                ? $"{Time(source.ScheduledCheckIn)}–{Time(source.ScheduledCheckOut)} / {Time(source.ScheduledBlock2CheckIn)}–{Time(source.ScheduledBlock2CheckOut)}"
                : source.ScheduledCheckIn.HasValue ? $"{Time(source.ScheduledCheckIn)}–{Time(source.ScheduledCheckOut)}" : "—";
            CheckIn = split
                ? $"{Punch(source.ActualCheckIn, source.MissingCheckIn)} / {Punch(source.ActualBlock2CheckIn, source.MissingBlock2CheckIn)}"
                : Punch(source.ActualCheckIn, source.MissingCheckIn);
            CheckOut = split
                ? $"{Punch(source.ActualBlock1CheckOut, source.MissingBlock1CheckOut)} / {Punch(source.ActualCheckOut, source.MissingCheckOut)}"
                : Punch(source.ActualCheckOut, source.MissingCheckOut);
            Worked = source.TotalWorkedMinutes > 0 ? FormatMinutes(source.TotalWorkedMinutes) : "—";
            Late = source.LateMinutes > 0 ? $"{source.LateMinutes} min" : "—";
            Overtime = source.RoundedOvertimeMinutes > 0 ? FormatMinutes(source.RoundedOvertimeMinutes) : "—";
            Notes = string.Join(Environment.NewLine, source.Notes);

            Badges = BuildBadges(source, split);

            // Caso 12: a quien no tiene extras autorizadas, RH le autoriza el excedente del día completo o en bloques de 30 min.
            NeedsOvertimeDecision = !source.EmployeeOvertimeAuthorized && source.RawOvertimeMinutes > 0;
            OvertimeAuthorizedForDay = source.OvertimeAuthorized && !source.OvertimeAuthorizationOutdated;
            if (NeedsOvertimeDecision)
            {
                OvertimeStatus = OvertimeAuthorizedForDay
                    ? $"Autorizadas: {FormatMinutes(source.AuthorizedOvertimeMinutes ?? source.RawOvertimeMinutes)}"
                    : $"Por autorizar: {FormatMinutes(source.RawOvertimeMinutes)}";
            }

            if (canAdjust && NeedsOvertimeDecision)
            {
                var actions = new List<RowAction>();
                if (OvertimeAuthorizedForDay)
                {
                    actions.Add(new RowAction("Retirar autorización de extras", Command(() => setOvertime(this, false, null))));
                }
                else
                {
                    var raw = source.RawOvertimeMinutes;
                    actions.Add(new RowAction($"Autorizar completo ({FormatMinutes(raw)})", Command(() => setOvertime(this, true, raw))));
                    foreach (var block in Enumerable.Range(1, raw / DailyAttendance.OvertimeAuthorizationBlockMinutes)
                                 .Select(k => k * DailyAttendance.OvertimeAuthorizationBlockMinutes).Where(m => m < raw))
                    {
                        actions.Add(new RowAction($"Autorizar {FormatMinutes(block)}", Command(() => setOvertime(this, true, block))));
                    }
                }
                OvertimeActions = actions;
            }

            if (canAdjust)
            {
                ExitMenus = source.TemporaryExits.Select(exit =>
                {
                    var actions = new List<RowAction>
                    {
                        new("Comida", Command(() => classifyExit(this, exit, TemporaryExitStatus.Lunch))),
                        new("Permiso con goce", Command(() => classifyExit(this, exit, TemporaryExitStatus.ApprovedPaid))),
                        new("Permiso sin goce (descontar)", Command(() => classifyExit(this, exit, TemporaryExitStatus.ApprovedUnpaid))),
                        new("Error de checada", Command(() => classifyExit(this, exit, TemporaryExitStatus.Dismissed)))
                    };
                    if (exit.Status != (exit.IsAutomaticLunch ? TemporaryExitStatus.Lunch : TemporaryExitStatus.Pending))
                        actions.Add(new RowAction("Quitar clasificación", Command(() => classifyExit(this, exit, null))));
                    return new ExitMenu(ExitLabel(exit), exit.Status == TemporaryExitStatus.Pending, actions);
                }).ToList();
            }
            else
            {
                ExitMenus = Array.Empty<ExitMenu>();
                ExitSummary = string.Join(Environment.NewLine, source.TemporaryExits.Select(ExitLabel));
            }
        }

        public AttendanceReportViewDto Source { get; }
        public string EmployeeId => Source.EmployeeId;
        public string EmployeeName => Source.EmployeeName;
        public DateTime Date => Source.Date;
        public string ShiftName => string.IsNullOrWhiteSpace(Source.ShiftName) ? "—" : Source.ShiftName;
        public string Scheduled { get; }
        public string CheckIn { get; }
        public string CheckOut { get; }
        public string Worked { get; }
        public string Late { get; }
        public string Overtime { get; }
        public string Notes { get; }
        public IReadOnlyList<StatusBadge> Badges { get; }

        public bool NeedsOvertimeDecision { get; }
        public bool OvertimeAuthorizedForDay { get; }
        public string? OvertimeStatus { get; }
        public IReadOnlyList<RowAction> OvertimeActions { get; } = Array.Empty<RowAction>();
        public bool HasOvertimeActions => OvertimeActions.Count > 0;

        public IReadOnlyList<ExitMenu> ExitMenus { get; }
        public string? ExitSummary { get; }

        /// <summary>Lo que RH tiene pendiente en el día: salidas sin clasificar o extras sin autorizar.</summary>
        public bool HasPendingDecision =>
            Source.TemporaryExits.Any(e => e.Status == TemporaryExitStatus.Pending) || (NeedsOvertimeDecision && !OvertimeAuthorizedForDay);

        public static string FormatMinutes(int minutes) =>
            minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00} min";

        private static IReadOnlyList<StatusBadge> BuildBadges(AttendanceReportViewDto r, bool split)
        {
            var badges = new List<StatusBadge>();
            if (r.IsAbsent) badges.Add(new("Falta", "danger"));
            if (r.LateMinutes > 0) badges.Add(new("Retardo", "warning"));
            if (split)
            {
                if (r.MissingCheckIn) badges.Add(new("Sin entrada B1", "warning"));
                if (r.MissingBlock1CheckOut) badges.Add(new("Sin salida B1", "warning"));
                if (r.MissingBlock2CheckIn) badges.Add(new("Sin entrada B2", "warning"));
                if (r.MissingCheckOut) badges.Add(new("Sin salida B2", "warning"));
            }
            else
            {
                if (r.MissingCheckIn) badges.Add(new("Sin entrada", "warning"));
                if (r.MissingCheckOut) badges.Add(new("Sin salida", "warning"));
            }
            if (r.WorkedOnRestDay) badges.Add(new("Trabajó en descanso", "success"));
            else if (r.IsRestDay) badges.Add(new("Descanso", "info"));
            if (badges.Count == 0 && r.HasPunches) badges.Add(new("Asistencia", "success"));
            return badges;
        }

        private static string ExitLabel(TemporaryExitDto exit) =>
            $"Salida {exit.ExitTime:HH:mm}–{exit.ReturnTime:HH:mm} · " + exit.Status switch
            {
                TemporaryExitStatus.Lunch => "Comida",
                TemporaryExitStatus.ApprovedPaid => "Con goce",
                TemporaryExitStatus.ApprovedUnpaid => "Sin goce",
                TemporaryExitStatus.Dismissed => "Error",
                _ => "Pendiente"
            };

        private static string Time(TimeSpan? time) => time?.ToString(@"hh\:mm") ?? "--";
        private static string Punch(DateTime? time, bool missing) => time?.ToString("HH:mm") ?? (missing ? "--" : "—");
        private static ICommand Command(Func<Task> action) => new DelegateCommand(async () => await action());
    }
}
