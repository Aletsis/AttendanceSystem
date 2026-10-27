using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using Prism.Dialogs;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.UpdateDailyShift;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceLogs;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using Permissions = AttendanceSystem.Application.Authorization.Permissions;

namespace AttendanceSystem.WPF.ViewModels.Attendance
{
    /// <summary>
    /// Bitácora de marcajes, como "Logs de asistencia" de la web: cada checada del día con cómo se usó (entrada, salida,
    /// bloque), si es manual, corregida o anulada, y el reloj de donde vino. RH puede reasignar un marcaje, cambiar el
    /// horario del día y regresar las decisiones manuales al roster; todo queda en el historial del día.
    /// </summary>
    public class PunchLogViewModel : ViewModelBase
    {
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly IAuthenticationStateService _authService;
        private readonly IReportExportService _reportExportService;
        private readonly IDialogService _dialogService;

        private DateTime _selectedDate = DateTime.Today;
        private EmployeeOption? _selectedEmployee;
        private ObservableCollection<PunchRowItem> _rows = new();
        private DayAdjustmentStateDto? _dayState;
        private bool _loaded;

        public PunchLogViewModel(
            IMessageService messageService,
            IMediator mediator,
            IAuthenticationStateService authService,
            IReportExportService reportExportService,
            IDialogService dialogService)
        {
            _messageService = messageService;
            _mediator = mediator;
            _authService = authService;
            _reportExportService = reportExportService;
            _dialogService = dialogService;

            RefreshCommand = new DelegateCommand(async () => await LoadAsync());
            ExportCommand = new DelegateCommand(async () => await ExportAsync());
            ManualEntryCommand = new DelegateCommand(OpenManualEntry);
            ClearShiftOverrideCommand = new DelegateCommand(async () => await ClearShiftOverrideAsync());
            ClearRestDayOverrideCommand = new DelegateCommand(async () => await ClearRestDayOverrideAsync());
            PreviousDayCommand = new DelegateCommand(() => SelectedDate = SelectedDate.AddDays(-1));
            NextDayCommand = new DelegateCommand(() => SelectedDate = SelectedDate.AddDays(1));

            _ = InitializeAsync();
        }

        public ObservableCollection<EmployeeOption> Employees { get; } = new();
        public ObservableCollection<RowAction> ShiftActions { get; } = new();
        public ObservableCollection<AttendanceHistoryEntryDto> History { get; } = new();

        public ObservableCollection<PunchRowItem> Rows { get => _rows; private set => SetProperty(ref _rows, value); }

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set { if (SetProperty(ref _selectedDate, value) && _loaded) _ = LoadAsync(); }
        }

        public EmployeeOption? SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (!SetProperty(ref _selectedEmployee, value)) return;
                RaisePropertyChanged(nameof(HasEmployee));
                RaisePropertyChanged(nameof(CanChangeShift));
                if (_loaded) _ = LoadAsync();
            }
        }

        public bool HasEmployee => SelectedEmployee is { Id.Length: > 0 };

        // Caso 13: sólo se muestran las acciones que el usuario puede hacer (los comandos también lo validan).
        public bool CanAdjust => _authService.HasPermission(Permissions.AttendanceAdjust);
        public bool CanViewHistory => _authService.HasPermission(Permissions.AttendanceViewHistory);
        public bool CanManualPunch => _authService.HasPermission(Permissions.AttendanceManualPunch);
        public bool CanChangeShift => CanAdjust && HasEmployee;

        public string? ShiftOverrideText => _dayState?.ShiftOverrideName is { } name ? $"Horario cambiado a mano: {name}" : null;
        public string? RestDayOverrideText => _dayState?.IsRestDayOverride is { } rest
            ? rest ? "Marcado a mano como descanso" : "Marcado a mano como laborable"
            : null;
        public bool HasShiftOverride => ShiftOverrideText != null;
        public bool HasRestDayOverride => RestDayOverrideText != null;
        public bool ShowHistory => CanViewHistory && History.Count > 0;
        public string HistoryHeader => $"Historial de ajustes del día ({History.Count})";
        public string Summary => Rows.Count == 0 ? "Sin marcajes" : $"{Rows.Count} marcaje(s) · {Rows.Count(r => r.Source.IsManual)} manual(es) · {Rows.Count(r => r.Source.IsVoided)} anulado(s)";

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ManualEntryCommand { get; }
        public ICommand ClearShiftOverrideCommand { get; }
        public ICommand ClearRestDayOverrideCommand { get; }
        public ICommand PreviousDayCommand { get; }
        public ICommand NextDayCommand { get; }

        private async Task InitializeAsync()
        {
            try
            {
                Employees.Add(new EmployeeOption(string.Empty, "Todos los empleados"));
                var employees = await _mediator.Send(new GetAllEmployeesQuery());
                if (employees.IsSuccess && employees.Value != null)
                {
                    foreach (var e in employees.Value.OrderBy(e => e.FullName))
                        Employees.Add(new EmployeeOption(e.Id, $"{e.Id} - {e.FullName}"));
                }
                _selectedEmployee = Employees[0];
                RaisePropertyChanged(nameof(SelectedEmployee));

                if (CanAdjust)
                {
                    var shifts = await _mediator.Send(new GetShiftsQuery());
                    if (shifts.IsSuccess)
                    {
                        foreach (var shift in shifts.Value.OrderBy(s => s.StartTime))
                            ShiftActions.Add(new RowAction($"{shift.Name} ({shift.StartTime:hh\\:mm}–{shift.EndTime:hh\\:mm})",
                                new DelegateCommand(async () => await ChangeShiftAsync(shift.Id, shift.Name))));
                    }
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"No se pudieron cargar los empleados: {ex.Message}");
            }
            _loaded = true;
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            SetBusy(true, "Cargando marcajes...");
            try
            {
                var employeeId = SelectedEmployee?.Id ?? string.Empty;
                var logs = await _mediator.Send(new GetAttendanceLogsQuery(SelectedDate, employeeId));
                Rows = new ObservableCollection<PunchRowItem>(logs
                    .OrderBy(l => l.CheckTime)
                    .Select(l => new PunchRowItem(l, CanAdjust, AssignAsync)));

                _dayState = null;
                History.Clear();
                if (HasEmployee)
                {
                    var date = DateOnly.FromDateTime(SelectedDate);
                    _dayState = await _mediator.Send(new GetDayAdjustmentStateQuery(employeeId, date));
                    if (CanViewHistory && _dayState.HistoryCount > 0)
                        // Los movimientos se guardan en UTC; se muestran en la hora local, como en la web.
                        History.AddRange((await _mediator.Send(new GetAttendanceHistoryQuery(employeeId, date)))
                            .Select(entry => entry with { ChangedAt = entry.ChangedAt.ToLocalTime() }));
                }
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al cargar los marcajes: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
                RaiseDayState();
            }
        }

        private void RaiseDayState()
        {
            RaisePropertyChanged(nameof(ShiftOverrideText));
            RaisePropertyChanged(nameof(RestDayOverrideText));
            RaisePropertyChanged(nameof(HasShiftOverride));
            RaisePropertyChanged(nameof(HasRestDayOverride));
            RaisePropertyChanged(nameof(ShowHistory));
            RaisePropertyChanged(nameof(HistoryHeader));
            RaisePropertyChanged(nameof(Summary));
        }

        private async Task AssignAsync(AttendanceLogViewDto record, string type)
        {
            // Caso 13 (como la web): asignar y desasignar exigen motivo; restaurar no.
            string? reason = null;
            if (type != "Restaurar")
            {
                reason = await _dialogService.AskReasonAsync(type == "None" ? "Desasignar marcaje" : $"Asignar como {type}",
                    $"{record.EmployeeName}: marcaje de las {record.CheckTime:HH:mm} del {SelectedDate:dd/MM/yyyy}.");
                if (reason == null) return;
            }

            // La decisión se toma sobre la jornada que se está consultando; la revisión sólo se conoce con el empleado elegido.
            var expected = SelectedEmployee?.Id == record.EmployeeId ? _dayState?.Revision : null;
            await ApplyAsync(() => _mediator.Send(new ManuallyAssignAttendanceCommand(
                record.EmployeeId, DateOnly.FromDateTime(SelectedDate), record.Id.ToString(), type,
                ExpectedRevision: expected, Reason: reason)));
        }

        private async Task ChangeShiftAsync(Guid shiftId, string shiftName)
        {
            if (SelectedEmployee is not { Id.Length: > 0 } employee) return;
            var reason = await _dialogService.AskReasonAsync("Cambiar horario del día",
                $"{employee.Display}: el {SelectedDate:dd/MM/yyyy} se calculará con \"{shiftName}\".");
            if (reason == null) return;

            SetBusy(true, "Cambiando horario...");
            try
            {
                var result = await _mediator.Send(new UpdateDailyShiftCommand(employee.Id, DateOnly.FromDateTime(SelectedDate), shiftId, reason));
                if (result.IsSuccess) await _messageService.ShowSuccessAsync("Horario del día actualizado.");
                else await _messageService.ShowErrorAsync(result.Error);
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"No se pudo cambiar el horario: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
            await LoadAsync();
        }

        private Task ClearShiftOverrideAsync() =>
            SelectedEmployee is { Id.Length: > 0 } employee
                ? ApplyAsync(() => _mediator.Send(new ClearDailyShiftOverrideCommand(employee.Id, DateOnly.FromDateTime(SelectedDate), ExpectedRevision: _dayState?.Revision)))
                : Task.CompletedTask;

        private Task ClearRestDayOverrideAsync() =>
            SelectedEmployee is { Id.Length: > 0 } employee
                ? ApplyAsync(() => _mediator.Send(new ClearDailyRestDayOverrideCommand(employee.Id, DateOnly.FromDateTime(SelectedDate), ExpectedRevision: _dayState?.Revision)))
                : Task.CompletedTask;

        /// <summary>Caso 13: si otra persona cambió el día mientras se veía, se avisa y se recarga para revisar y volver a aplicar.</summary>
        private async Task ApplyAsync(Func<Task<Result<AttendanceDecisionOutcome>>> send)
        {
            SetBusy(true, "Guardando...");
            Result<AttendanceDecisionOutcome> result;
            try
            {
                result = await send();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                await _messageService.ShowErrorAsync($"No se pudo guardar: {ex.Message}");
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

        private void OpenManualEntry()
        {
            _dialogService.ShowDialog("ManualPunchDialog", null, async result =>
            {
                if (result.Result != ButtonResult.OK) return;
                var checkTime = result.Parameters.GetValue<DateTime>("CheckTime");
                await _messageService.ShowSuccessAsync($"Marcaje registrado: {result.Parameters.GetValue<string>("EmployeeName")}, {checkTime:dd/MM/yyyy HH:mm}.");
                await LoadAsync();
            });
        }

        private async Task ExportAsync()
        {
            if (Rows.Count == 0)
            {
                await _messageService.ShowWarningAsync("No hay marcajes para exportar.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = $"Marcajes_{SelectedDate:yyyyMMdd}.xlsx",
                Title = "Guardar marcajes"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var data = Rows.Select(r => r.Source).ToList();
                var bytes = await Task.Run(() => _reportExportService.GenerateAttendanceLogsExcel(data));
                await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
                await _messageService.ShowSuccessAsync("Marcajes exportados correctamente.");
            }
            catch (Exception ex)
            {
                await _messageService.ShowErrorAsync($"Error al exportar: {ex.Message}");
            }
        }
    }

    /// <summary>Un marcaje del día, listo para mostrar.</summary>
    public sealed class PunchRowItem
    {
        public PunchRowItem(AttendanceLogViewDto source, bool canAdjust, Func<AttendanceLogViewDto, string, Task> assign)
        {
            Source = source;
            Assignment = source.AssignedDate is { } assigned && assigned.Date != source.CheckTime.Date
                ? $"{source.EntryType} ({assigned:dd/MM})"
                : source.EntryType;

            if (source.IsVoided)
            {
                VoidText = source.WasCorrected ? "Corregido (no cuenta)" : "Anulado (no cuenta)";
                VoidTooltip = $"{(source.WasCorrected ? "Corregido" : "Anulado")} por {source.VoidedBy}: {source.VoidReason}";
            }
            if (source.IsManual) ManualTooltip = $"Manual: {source.RegisteredBy} — {source.ManualReason}";

            // Un marcaje anulado ya no cuenta: no se reasigna.
            if (canAdjust && !source.IsVoided)
            {
                Actions = new[]
                {
                    Action("Asignar como entrada (B1)", "Entrada"),
                    Action("Asignar como salida B1", "Salida B1"),
                    Action("Asignar como entrada B2", "Entrada B2"),
                    Action("Asignar como salida (B2)", "Salida"),
                    Action("Desasignar (no usar en el cálculo)", "None"),
                    Action("Restaurar el cálculo automático", "Restaurar")
                };
            }

            RowAction Action(string header, string type) => new(header, new DelegateCommand(async () => await assign(source, type)));
        }

        public AttendanceLogViewDto Source { get; }
        public string EmployeeId => Source.EmployeeId;
        public string EmployeeName => Source.EmployeeName;
        public DateTime CheckTime => Source.CheckTime;
        public string Assignment { get; }
        public string Kind => Source.EntryType.StartsWith("Entrada", StringComparison.OrdinalIgnoreCase) ? "in"
            : Source.EntryType.StartsWith("Salida", StringComparison.OrdinalIgnoreCase) ? "out" : "none";
        public string VerifyMethod => Source.VerifyMethod;
        public string DeviceName => string.IsNullOrWhiteSpace(Source.DeviceName) ? "—" : Source.DeviceName;
        public string Status => Source.Status;
        public bool IsVoided => Source.IsVoided;
        public string? VoidText { get; }
        public string? VoidTooltip { get; }
        public bool IsManual => Source.IsManual;
        public string? ManualTooltip { get; }
        public bool IsCorrection => Source.IsCorrection;
        public IReadOnlyList<RowAction> Actions { get; } = Array.Empty<RowAction>();
        public bool HasActions => Actions.Count > 0;
    }
}
