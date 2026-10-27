using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using MahApps.Metro.IconPacks;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.WPF.ViewModels.Dashboard
{
    /// <summary>Inicio: resumen del día y accesos a las pantallas que el usuario puede abrir.</summary>
    public class DashboardViewModel : ViewModelBase
    {
        private readonly IAuthenticationStateService _authService;
        private readonly IFrameNavigationService _navigationService;
        private readonly IMediator _mediator;

        private int _activeEmployees;
        private int _presentToday;
        private int _absentToday;
        private int _lateToday;
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

        public int ActiveEmployees { get => _activeEmployees; set => SetProperty(ref _activeEmployees, value); }
        public int PresentToday { get => _presentToday; set => SetProperty(ref _presentToday, value); }
        public int AbsentToday { get => _absentToday; set => SetProperty(ref _absentToday, value); }
        public int LateToday { get => _lateToday; set => SetProperty(ref _lateToday, value); }

        /// <summary>Motivo por el que no se cargaron las cifras; antes el error se ocultaba y se veían ceros.</summary>
        public string? StatsError { get => _statsError; set => SetProperty(ref _statsError, value); }

        public ObservableCollection<QuickLink> QuickLinks { get; } = new();

        public ICommand OpenCommand { get; }
        public ICommand RefreshCommand { get; }

        public DashboardViewModel(
            IAuthenticationStateService authService,
            IFrameNavigationService navigationService,
            IMediator mediator)
        {
            _authService = authService;
            _navigationService = navigationService;
            _mediator = mediator;

            OpenCommand = new DelegateCommand<QuickLink>(link => { if (link != null) _navigationService.NavigateTo(link.ViewName); });
            RefreshCommand = new DelegateCommand(async () => await LoadDashboardAsync());

            var links = new[]
            {
                new QuickLink("Empleados", "Altas, cambios y bajas del personal", PackIconMaterialKind.AccountMultipleOutline, nameof(Views.Employees.EmployeesView)),
                new QuickLink("Asistencia", "Asistencia por día, cálculo y exportación", PackIconMaterialKind.CalendarCheckOutline, nameof(Views.Attendance.AttendanceView)),
                new QuickLink("Reportes", "Reportes de asistencia y nómina", PackIconMaterialKind.ChartBar, nameof(Views.Reports.ReportsView)),
                new QuickLink("Relojes y descargas", "Descarga de checadas de los relojes", PackIconMaterialKind.Fingerprint, nameof(Views.Devices.DevicesView)),
                new QuickLink("Turnos", "Horarios y turnos de trabajo", PackIconMaterialKind.ClockOutline, nameof(Views.Shifts.ShiftsView)),
                new QuickLink("Sucursales", "Sucursales internas y externas", PackIconMaterialKind.OfficeBuildingOutline, nameof(Views.Branches.BranchesView)),
                new QuickLink("Configuración", "Empresa, periodos y descargas automáticas", PackIconMaterialKind.CogOutline, nameof(Views.Settings.SettingsView)),
                new QuickLink("Respaldos", "Crear y restaurar respaldos", PackIconMaterialKind.DatabaseOutline, nameof(Views.Backup.BackupView)),
            };
            foreach (var link in links.Where(l => _authService.CanOpen(l.ViewName))) QuickLinks.Add(link);

            _ = LoadDashboardAsync();
        }

        private async Task LoadDashboardAsync()
        {
            SetBusy(true, "Cargando resumen del día...");
            StatsError = null;
            try
            {
                var empResult = await _mediator.Send(new GetAllEmployeesQuery());
                if (empResult.IsSuccess && empResult.Value != null)
                {
                    ActiveEmployees = empResult.Value.Count(e => e.Status == EmployeeStatus.Alta);
                }

                var today = DateTime.Today;
                var attendance = await _mediator.Send(new GetDailyAttendanceByDateRangeQuery(today, today));
                if (attendance != null)
                {
                    PresentToday = attendance.Count(a => a.ActualCheckIn.HasValue && !a.IsAbsent);
                    AbsentToday = attendance.Count(a => a.IsAbsent);
                    LateToday = attendance.Count(a => a.LateMinutes > 0);
                }
            }
            catch (Exception ex)
            {
                StatsError = $"No se pudo cargar el resumen del día: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    public sealed record QuickLink(string Title, string Description, PackIconMaterialKind Icon, string ViewName);
}
