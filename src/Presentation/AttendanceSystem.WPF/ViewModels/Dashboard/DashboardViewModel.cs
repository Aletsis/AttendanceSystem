using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Commands;
using AttendanceSystem.WPF.Services;
using MediatR;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;

namespace AttendanceSystem.WPF.ViewModels.Dashboard
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly IAuthenticationStateService _authService;
        private readonly IFrameNavigationService _navigationService;
        private readonly IMediator _mediator;

        private string _welcomeMessage = string.Empty;
        private int _totalEmployees;
        private int _presentToday;
        private int _absentToday;
        private int _lateToday;

        public string WelcomeMessage { get => _welcomeMessage; set => SetProperty(ref _welcomeMessage, value); }
        public int TotalEmployees { get => _totalEmployees; set => SetProperty(ref _totalEmployees, value); }
        public int PresentToday { get => _presentToday; set => SetProperty(ref _presentToday, value); }
        public int AbsentToday { get => _absentToday; set => SetProperty(ref _absentToday, value); }
        public int LateToday { get => _lateToday; set => SetProperty(ref _lateToday, value); }

        public ICommand NavigateToEmployeesCommand { get; }
        public ICommand NavigateToAttendanceCommand { get; }
        public ICommand NavigateToDepartmentsCommand { get; }
        public ICommand NavigateToPositionsCommand { get; }
        public ICommand NavigateToBranchesCommand { get; }
        public ICommand NavigateToShiftsCommand { get; }
        public ICommand NavigateToDevicesCommand { get; }
        public ICommand NavigateToReportsCommand { get; }
        public ICommand NavigateToSettingsCommand { get; }
        public ICommand NavigateToBackupCommand { get; }
        public ICommand LogoutCommand { get; }

        public DashboardViewModel(
            IAuthenticationStateService authService,
            IFrameNavigationService navigationService,
            IMediator mediator)
        {
            _authService = authService;
            _navigationService = navigationService;
            _mediator = mediator;

            NavigateToEmployeesCommand = new DelegateCommand(() => NavigateTo("Employees"));
            NavigateToAttendanceCommand = new DelegateCommand(() => NavigateTo("Attendance"));
            NavigateToDepartmentsCommand = new DelegateCommand(() => NavigateTo("Departments"));
            NavigateToPositionsCommand = new DelegateCommand(() => NavigateTo("Positions"));
            NavigateToBranchesCommand = new DelegateCommand(() => NavigateTo("Branches"));
            NavigateToShiftsCommand = new DelegateCommand(() => NavigateTo("Shifts"));
            NavigateToDevicesCommand = new DelegateCommand(() => NavigateTo("Devices"));
            NavigateToReportsCommand = new DelegateCommand(() => NavigateTo("Reports"));
            NavigateToSettingsCommand = new DelegateCommand(() => NavigateTo("Settings"));
            NavigateToBackupCommand = new DelegateCommand(() => NavigateTo("Backup"));
            LogoutCommand = new DelegateCommand(async () => await ExecuteLogoutAsync());

            _ = LoadDashboardAsync();
        }

        private async Task LoadDashboardAsync()
        {
            WelcomeMessage = $"Bienvenido, {_authService.CurrentUserName ?? "Usuario"}";
            
            SetBusy(true, "Cargando estadísticas...");
            try
            {
                // 1. Total Employees
                var empResult = await _mediator.Send(new GetAllEmployeesQuery());
                if (empResult.IsSuccess && empResult.Value != null)
                {
                    TotalEmployees = empResult.Value.Count();
                }

                // 2. Attendance Stats (Today)
                var today = DateTime.Today;
                var attQuery = new GetDailyAttendanceByDateRangeQuery(today, today);
                var attResult = await _mediator.Send(attQuery);
                
                if (attResult != null)
                {
                    PresentToday = attResult.Count(a => a.ActualCheckIn.HasValue && !a.IsAbsent);
                    AbsentToday = attResult.Count(a => a.IsAbsent);
                    LateToday = attResult.Count(a => a.LateMinutes > 0);
                }
            }
            catch (Exception)
            {
                // Silent catch for dashboard if fails, just show 0s or keep previous state
                // Or log error
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void NavigateTo(string viewName)
        {
             switch (viewName)
            {
                case "Employees": _navigationService.NavigateTo<Views.Employees.EmployeesView>(); break;
                case "Departments": _navigationService.NavigateTo<Views.Departments.DepartmentsView>(); break;
                case "Positions": _navigationService.NavigateTo<Views.Positions.PositionsView>(); break;
                case "Branches": _navigationService.NavigateTo<Views.Branches.BranchesView>(); break;
                case "Shifts": _navigationService.NavigateTo<Views.Shifts.ShiftsView>(); break;
                case "Devices": _navigationService.NavigateTo<Views.Devices.DevicesView>(); break;
                case "Attendance": _navigationService.NavigateTo<Views.Attendance.AttendanceView>(); break;
                case "Reports": _navigationService.NavigateTo<Views.Reports.ReportsView>(); break;
                case "Settings": _navigationService.NavigateTo<Views.Settings.SettingsView>(); break;
                case "Backup": _navigationService.NavigateTo<Views.Backup.BackupView>(); break;
            }
        }

        private async Task ExecuteLogoutAsync()
        {
            await _authService.LogoutAsync();
            _navigationService.NavigateTo<Views.Auth.LoginView>();
        }
    }
}
