using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Queries.GetUnmatchedPunches;
using AttendanceSystem.Application.Features.Devices.Queries.GetAllDevices;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Application.Features.Shifts.Commands.CheckScheduleIntegrity;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Permissions = AttendanceSystem.Application.Authorization.Permissions;

namespace AttendanceSystem.WPF.ViewModels.Dashboard
{
    /// <summary>
    /// Inicio con métricas (como el panel de la web): asistencia de hoy y de la semana, salud de los relojes y lo que
    /// RH tiene pendiente. Cada bloque se muestra sólo a quien puede ver esos datos.
    /// </summary>
    public class DashboardViewModel : ViewModelBase
    {
        private readonly IAuthenticationStateService _authService;
        private readonly IFrameNavigationService _navigationService;
        private readonly IMediator _mediator;

        private int _activeEmployees;
        private int _onTimeToday;
        private int _lateToday;
        private int _absentToday;
        private int _withoutRecordToday;
        private int _expectedToday;
        private int _onlineDevices;
        private int _totalDevices;
        private int _almostFullDevices;
        private string? _oldestDownloadText;
        private int _pendingExits;
        private int _pendingOvertime;
        private int _unmatchedPunches;
        private string? _statsError;

        public string WelcomeMessage => $"Hola, {_authService.CurrentUserName ?? "usuario"}";
        public string RoleName => string.Join(", ", _authService.CurrentUserRoles);
        public string TodayText
        {
            get
            {
                var text = DateTime.Today.ToString("dddd d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-MX"));
                return char.ToUpper(text[0]) + text[1..];
            }
        }

        // Caso 13: cada bloque sólo para quien puede ver esos datos.
        public bool ShowAttendance => _authService.HasPermission(Permissions.AttendanceView);
        public bool ShowDevices => _authService.HasPermission(Permissions.DevicesDownload);
        public bool ShowUnmatched => _authService.HasPermission(Permissions.EmployeesView);

        public int ActiveEmployees { get => _activeEmployees; private set => SetProperty(ref _activeEmployees, value); }
        public int OnTimeToday { get => _onTimeToday; private set => SetProperty(ref _onTimeToday, value); }
        public int LateToday { get => _lateToday; private set => SetProperty(ref _lateToday, value); }
        public int AbsentToday { get => _absentToday; private set => SetProperty(ref _absentToday, value); }
        /// <summary>Con turno hoy y sin checadas todavía (aún no es falta mientras no termine su jornada).</summary>
        public int WithoutRecordToday { get => _withoutRecordToday; private set => SetProperty(ref _withoutRecordToday, value); }
        public int PresentToday => OnTimeToday + LateToday;
        /// <summary>Puntualidad: puntuales entre los que tienen turno hoy.</summary>
        public double PunctualityRate => _expectedToday == 0 ? 0 : Math.Round(OnTimeToday * 100.0 / _expectedToday, 1);
        public string PunctualityText => _expectedToday == 0 ? "Sin turnos calculados hoy" : $"{PunctualityRate:0.#}% puntual de {_expectedToday} con turno hoy";

        public ObservableCollection<DayBar> WeekBars { get; } = new();
        public bool HasWeekData => WeekBars.Any(b => b.Expected > 0);

        public int OnlineDevices { get => _onlineDevices; private set => SetProperty(ref _onlineDevices, value); }
        public int TotalDevices { get => _totalDevices; private set => SetProperty(ref _totalDevices, value); }
        public int AlmostFullDevices { get => _almostFullDevices; private set => SetProperty(ref _almostFullDevices, value); }
        public string? OldestDownloadText { get => _oldestDownloadText; private set => SetProperty(ref _oldestDownloadText, value); }
        public ObservableCollection<DeviceHealthItem> Devices { get; } = new();
        public string DevicesHealthText => TotalDevices == 0 ? "No hay relojes registrados."
            : OnlineDevices == TotalDevices && AlmostFullDevices == 0 ? "Todos los relojes en línea y con memoria disponible."
            : string.Join(" ", new[]
            {
                OnlineDevices < TotalDevices ? $"{TotalDevices - OnlineDevices} sin conexión." : null,
                AlmostFullDevices > 0 ? $"{AlmostFullDevices} con la memoria casi llena." : null
            }.Where(t => t != null));
        public bool DevicesNeedAttention => TotalDevices > 0 && (OnlineDevices < TotalDevices || AlmostFullDevices > 0);

        public int PendingExits { get => _pendingExits; private set => SetProperty(ref _pendingExits, value); }
        public int PendingOvertime { get => _pendingOvertime; private set => SetProperty(ref _pendingOvertime, value); }
        public int UnmatchedPunches { get => _unmatchedPunches; private set => SetProperty(ref _unmatchedPunches, value); }
        public ObservableCollection<string> ScheduleAlerts { get; } = new();
        public bool HasScheduleAlerts => ScheduleAlerts.Count > 0;
        public bool HasPending => PendingExits > 0 || PendingOvertime > 0 || UnmatchedPunches > 0 || HasScheduleAlerts;

        /// <summary>Motivo por el que no se cargaron las cifras; antes el error se ocultaba y se veían ceros.</summary>
        public string? StatsError { get => _statsError; private set => SetProperty(ref _statsError, value); }

        public ICommand RefreshCommand { get; }
        public ICommand OpenAttendanceCommand { get; }
        public ICommand OpenDevicesCommand { get; }
        public ICommand OpenEmployeesCommand { get; }

        public DashboardViewModel(
            IAuthenticationStateService authService,
            IFrameNavigationService navigationService,
            IMediator mediator)
        {
            _authService = authService;
            _navigationService = navigationService;
            _mediator = mediator;

            RefreshCommand = new DelegateCommand(async () => await LoadDashboardAsync());
            OpenAttendanceCommand = new DelegateCommand(() => _navigationService.NavigateTo(nameof(Views.Attendance.AttendanceView)));
            OpenDevicesCommand = new DelegateCommand(() => _navigationService.NavigateTo(nameof(Views.Devices.DevicesView)));
            OpenEmployeesCommand = new DelegateCommand(() => _navigationService.NavigateTo(nameof(Views.Employees.EmployeesView)));

            _ = LoadDashboardAsync();
        }

        private async Task LoadDashboardAsync()
        {
            SetBusy(true, "Cargando el resumen...");
            StatsError = null;
            var problems = new List<string>();
            try
            {
                var empResult = await _mediator.Send(new GetAllEmployeesQuery());
                if (empResult.IsSuccess && empResult.Value != null)
                    ActiveEmployees = empResult.Value.Count(e => e.Status == EmployeeStatus.Alta);

                if (ShowAttendance) await Try(LoadAttendanceAsync, "asistencia", problems);
                if (ShowDevices) await Try(LoadDevicesAsync, "relojes", problems);
                await Try(LoadAlertsAsync, "avisos", problems);
            }
            catch (Exception ex)
            {
                problems.Add(ex.Message);
            }
            finally
            {
                if (problems.Count > 0) StatsError = $"No se pudo cargar todo el resumen: {string.Join("; ", problems)}";
                RaiseAll();
                SetBusy(false);
            }
        }

        private static async Task Try(Func<Task> load, string what, List<string> problems)
        {
            try { await load(); }
            catch (Exception ex) { problems.Add($"{what} ({ex.Message})"); }
        }

        private async Task LoadAttendanceAsync()
        {
            var today = DateTime.Today;
            var week = (await _mediator.Send(new GetAttendanceReportQuery(today.AddDays(-6), today))).ToList();

            // Hoy: con turno = no es descanso (o trabajó en su descanso).
            var todayRows = week.Where(r => r.Date.Date == today && (!r.IsRestDay || r.WorkedOnRestDay)).ToList();
            _expectedToday = todayRows.Count;
            OnTimeToday = todayRows.Count(r => r.HasPunches && !r.IsAbsent && r.LateMinutes == 0 && !r.MissingCheckIn);
            LateToday = todayRows.Count(r => r.LateMinutes > 0);
            AbsentToday = todayRows.Count(r => r.IsAbsent);
            WithoutRecordToday = todayRows.Count(r => !r.HasPunches && !r.IsAbsent);

            PendingExits = week.Count(r => r.TemporaryExits.Any(e => e.Status == TemporaryExitStatus.Pending));
            PendingOvertime = week.Count(r => !r.EmployeeOvertimeAuthorized && r.RawOvertimeMinutes > 0
                                              && !(r.OvertimeAuthorized && !r.OvertimeAuthorizationOutdated));

            WeekBars.Clear();
            for (var day = today.AddDays(-6); day <= today; day = day.AddDays(1))
            {
                var rows = week.Where(r => r.Date.Date == day && (!r.IsRestDay || r.WorkedOnRestDay)).ToList();
                var present = rows.Count(r => r.HasPunches && !r.IsAbsent);
                WeekBars.Add(new DayBar(day, rows.Count, present, rows.Count(r => r.IsAbsent), rows.Count(r => r.LateMinutes > 0), day == today));
            }
        }

        private async Task LoadDevicesAsync()
        {
            var result = await _mediator.Send(new GetAllDevicesQuery());
            if (!result.IsSuccess || result.Value == null) throw new InvalidOperationException(result.Error);

            var active = result.Value.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
            TotalDevices = active.Count;
            OnlineDevices = active.Count(IsOnline);
            AlmostFullDevices = active.Count(IsAlmostFull);
            var oldest = active.Where(d => d.DownloadMethod == DeviceDownloadMethod.Sdk).Select(d => d.LastDownloadAt).OrderBy(d => d ?? DateTime.MinValue).FirstOrDefault();
            OldestDownloadText = active.Any(d => d.DownloadMethod == DeviceDownloadMethod.Sdk)
                ? oldest is { } at ? $"Descarga más antigua: {at.ToLocalTime():dd/MM HH:mm}" : "Hay relojes que nunca se han descargado."
                : null;

            Devices.Clear();
            foreach (var device in active.OrderBy(d => IsOnline(d)).ThenByDescending(IsAlmostFull).Take(6))
                Devices.Add(new DeviceHealthItem(device.Name, device.Status, IsOnline(device), IsAlmostFull(device)));
        }

        private async Task LoadAlertsAsync()
        {
            ScheduleAlerts.Clear();
            var alerts = await _mediator.Send(new GetScheduleAlertsQuery()) ?? Array.Empty<ScheduleAlertDto>();
            foreach (var alert in alerts.Take(5)) ScheduleAlerts.Add(alert.Message);

            if (ShowUnmatched)
            {
                var to = DateOnly.FromDateTime(DateTime.Today);
                var from = to.AddDays(-GetUnmatchedPunchesQueryHandler.DefaultLookbackDays);
                UnmatchedPunches = (await _mediator.Send(new GetUnmatchedPunchesQuery(from, to)))?.Count ?? 0;
            }
        }

        private static bool IsOnline(DeviceDto d) => d.Status is "En Línea" or "Online";
        private static bool IsAlmostFull(DeviceDto d) =>
            d.AttendanceRecordCapacity is > 0 && (d.AttendanceRecordCount ?? 0) >= d.AttendanceRecordCapacity.Value * 0.9;

        private void RaiseAll()
        {
            RaisePropertyChanged(nameof(PresentToday));
            RaisePropertyChanged(nameof(PunctualityRate));
            RaisePropertyChanged(nameof(PunctualityText));
            RaisePropertyChanged(nameof(HasWeekData));
            RaisePropertyChanged(nameof(DevicesHealthText));
            RaisePropertyChanged(nameof(DevicesNeedAttention));
            RaisePropertyChanged(nameof(HasScheduleAlerts));
            RaisePropertyChanged(nameof(HasPending));
        }
    }

    /// <summary>Un día de la gráfica semanal: porcentaje de asistencia de quienes tenían turno.</summary>
    public sealed class DayBar
    {
        /// <summary>Alto del área de la gráfica, en píxeles.</summary>
        public const double ChartHeight = 120;

        public DayBar(DateTime date, int expected, int present, int absent, int late, bool isToday)
        {
            Date = date;
            Expected = expected;
            Rate = expected == 0 ? 0 : present * 100.0 / expected;
            IsToday = isToday;
            Label = isToday ? "Hoy" : date.ToString("ddd d", CultureInfo.GetCultureInfo("es-MX"));
            // Los días sin turnos calculados se ven como una base mínima, no como 0 % de asistencia.
            BarHeight = expected == 0 ? 2 : Math.Max(4, Rate / 100 * ChartHeight);
            ValueText = expected == 0 ? "—" : $"{Rate:0}%";
            Tooltip = expected == 0
                ? $"{date:dddd dd/MM}: sin asistencia calculada"
                : $"{date:dddd dd/MM}: {Rate:0}% de asistencia\n{present} de {expected} con turno · {absent} falta(s) · {late} retardo(s)";
        }

        public DateTime Date { get; }
        public int Expected { get; }
        public double Rate { get; }
        public bool IsToday { get; }
        public string Label { get; }
        public double BarHeight { get; }
        public string ValueText { get; }
        public string Tooltip { get; }
    }

    public sealed record DeviceHealthItem(string Name, string Status, bool IsOnline, bool IsAlmostFull);
}
