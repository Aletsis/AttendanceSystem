using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.WPF.Services;
using MahApps.Metro.IconPacks;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace AttendanceSystem.WPF.ViewModels
{
    /// <summary>
    /// Ventana principal: menú lateral (sólo las pantallas que el usuario puede abrir), usuario actual, cierre de
    /// sesión y aviso de mantenimiento de la base de datos.
    /// </summary>
    public class ShellViewModel : BindableBase
    {
        private readonly IAuthenticationStateService _authService;
        private readonly IFrameNavigationService _navigationService;
        private readonly IRegionManager _regionManager;
        private readonly IRestoreStateService _restoreState;
        private readonly Prism.Dialogs.IDialogService _dialogService;
        private readonly IMessageService _messageService;
        private readonly DispatcherTimer _maintenanceTimer;
        private string? _currentView;
        private string? _maintenanceMessage;
        private bool _maintenanceIsFailure;
        private bool _attached;

        public ShellViewModel(
            IAuthenticationStateService authService,
            IFrameNavigationService navigationService,
            IRegionManager regionManager,
            IRestoreStateService restoreState,
            Prism.Dialogs.IDialogService dialogService,
            IMessageService messageService)
        {
            _dialogService = dialogService;
            _messageService = messageService;
            _authService = authService;
            _navigationService = navigationService;
            _regionManager = regionManager;
            _restoreState = restoreState;

            NavigateCommand = new DelegateCommand<NavItem>(item => { if (item?.ViewName != null) _navigationService.NavigateTo(item.ViewName); });
            LogoutCommand = new DelegateCommand(async () => await LogoutAsync());
            DismissToastCommand = new DelegateCommand<ToastMessage>(toast => _messageService.Toasts.Remove(toast));
            ChangePasswordCommand = new DelegateCommand(() => _dialogService.ShowDialog("ChangePasswordDialog", null, async result =>
            {
                if (result.Result == Prism.Dialogs.ButtonResult.OK)
                    await _messageService.ShowSuccessAsync("Su contraseña se cambió correctamente.");
            }));

            _authService.SessionChanged += (_, _) => OnSessionChanged();

            // El estado de mantenimiento lo actualiza RestoreStateService consultando PostgreSQL; aquí sólo se refleja.
            _maintenanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _maintenanceTimer.Tick += (_, _) => RefreshMaintenance();
            _maintenanceTimer.Start();
            RefreshMaintenance();
        }

        public ObservableCollection<NavItem> NavItems { get; } = new();

        public bool IsAuthenticated => _authService.IsAuthenticated;
        public string UserName => _authService.CurrentUserName ?? string.Empty;
        public string RoleName => string.Join(", ", _authService.CurrentUserRoles);
        public string Initials => string.IsNullOrWhiteSpace(UserName) ? "?" : UserName.Trim()[..1].ToUpperInvariant();

        /// <summary>Texto del aviso de mantenimiento; null en operación normal.</summary>
        public string? MaintenanceMessage { get => _maintenanceMessage; private set => SetProperty(ref _maintenanceMessage, value); }
        public bool MaintenanceIsFailure { get => _maintenanceIsFailure; private set => SetProperty(ref _maintenanceIsFailure, value); }

        public ICommand NavigateCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ChangePasswordCommand { get; }
        public ICommand DismissToastCommand { get; }

        public ObservableCollection<ToastMessage> Toasts => _messageService.Toasts;

        /// <summary>Lo llama la ventana al cargarse, cuando la región principal ya existe.</summary>
        public void AttachToMainRegion()
        {
            if (_attached || !_regionManager.Regions.ContainsRegionWithName(FrameNavigationService.MainRegionName)) return;
            _attached = true;
            _regionManager.Regions[FrameNavigationService.MainRegionName].NavigationService.Navigated += (_, e) =>
            {
                _currentView = e.Uri.OriginalString.Split('?')[0];
                UpdateSelection();
            };
        }

        private void OnSessionChanged()
        {
            RaisePropertyChanged(nameof(IsAuthenticated));
            RaisePropertyChanged(nameof(UserName));
            RaisePropertyChanged(nameof(RoleName));
            RaisePropertyChanged(nameof(Initials));
            BuildMenu();
        }

        private void BuildMenu()
        {
            NavItems.Clear();
            if (!_authService.IsAuthenticated) return;

            var sections = new (string? Header, NavItem[] Items)[]
            {
                (null, new[]
                {
                    new NavItem("Inicio", PackIconMaterialKind.ViewDashboardOutline, nameof(Views.Dashboard.DashboardView)),
                    new NavItem("Empleados", PackIconMaterialKind.AccountMultipleOutline, nameof(Views.Employees.EmployeesView)),
                    new NavItem("Asistencia", PackIconMaterialKind.CalendarCheckOutline, nameof(Views.Attendance.AttendanceView)),
                    new NavItem("Reportes", PackIconMaterialKind.ChartBar, nameof(Views.Reports.ReportsView)),
                }),
                ("CATÁLOGOS", new[]
                {
                    new NavItem("Sucursales", PackIconMaterialKind.OfficeBuildingOutline, nameof(Views.Branches.BranchesView)),
                    new NavItem("Departamentos", PackIconMaterialKind.Domain, nameof(Views.Departments.DepartmentsView)),
                    new NavItem("Puestos", PackIconMaterialKind.BadgeAccountOutline, nameof(Views.Positions.PositionsView)),
                    new NavItem("Turnos", PackIconMaterialKind.ClockOutline, nameof(Views.Shifts.ShiftsView)),
                }),
                ("DISPOSITIVOS", new[]
                {
                    new NavItem("Relojes y descargas", PackIconMaterialKind.Fingerprint, nameof(Views.Devices.DevicesView)),
                }),
                ("SISTEMA", new[]
                {
                    new NavItem("Configuración", PackIconMaterialKind.CogOutline, nameof(Views.Settings.SettingsView)),
                    new NavItem("Respaldos", PackIconMaterialKind.DatabaseOutline, nameof(Views.Backup.BackupView)),
                }),
            };

            foreach (var (header, items) in sections)
            {
                var visible = items.Where(i => _authService.CanOpen(i.ViewName!)).ToList();
                if (visible.Count == 0) continue;
                if (header != null) NavItems.Add(NavItem.Header(header));
                foreach (var item in visible) NavItems.Add(item);
            }
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            // El detalle de un empleado pertenece a "Empleados".
            var current = _currentView == nameof(Views.Employees.EmployeeDetailView) ? nameof(Views.Employees.EmployeesView) : _currentView;
            foreach (var item in NavItems) item.IsSelected = item.ViewName != null && item.ViewName == current;
        }

        private async Task LogoutAsync()
        {
            await _authService.LogoutAsync();
            _navigationService.ResetTo(FrameNavigationService.LoginViewName);
        }

        private void RefreshMaintenance()
        {
            var state = _restoreState.CurrentState;
            MaintenanceIsFailure = state == MaintenanceState.Failed;
            MaintenanceMessage = state switch
            {
                MaintenanceState.Normal => null,
                MaintenanceState.Failed =>
                    "El sistema está en mantenimiento: la base de datos no quedó en un estado válido (restauración o actualización fallida). " +
                    "No se pueden guardar cambios hasta que un administrador restaure un respaldo.",
                _ => "Hay una restauración de la base de datos en curso. Los cambios están bloqueados hasta que termine; al terminar, vuelva a iniciar sesión.",
            };
        }
    }

    public class NavItem : BindableBase
    {
        private bool _isSelected;

        public NavItem(string title, PackIconMaterialKind icon, string? viewName)
        {
            Title = title;
            Icon = icon;
            ViewName = viewName;
        }

        public static NavItem Header(string title) => new(title, PackIconMaterialKind.None, null) { IsHeader = true };

        public string Title { get; }
        public PackIconMaterialKind Icon { get; }
        public string? ViewName { get; }
        public bool IsHeader { get; private init; }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }
}
