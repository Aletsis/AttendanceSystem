using AttendanceSystem.Application.Authorization;

namespace AttendanceSystem.WPF.Services
{
    /// <summary>
    /// Caso 13: permiso que exige cada pantalla, el mismo que la página equivalente de la versión web. Lo usan el menú
    /// (para mostrarla) y la navegación (para no abrirla aunque se llegue por otro camino, p. ej. el historial).
    /// </summary>
    public static class PagePermissions
    {
        private static readonly Dictionary<string, string> Required = new()
        {
            [nameof(Views.Employees.EmployeesView)] = Permissions.EmployeesView,
            [nameof(Views.Employees.EmployeeDetailView)] = Permissions.EmployeesView,
            [nameof(Views.Attendance.AttendanceView)] = Permissions.AttendanceView,
            [nameof(Views.Attendance.PunchLogView)] = Permissions.AttendanceView,
            [nameof(Views.Reports.ReportsView)] = Permissions.ReportsView,
            [nameof(Views.Branches.BranchesView)] = Permissions.CatalogsView,
            [nameof(Views.Departments.DepartmentsView)] = Permissions.CatalogsView,
            [nameof(Views.Positions.PositionsView)] = Permissions.CatalogsView,
            [nameof(Views.Shifts.ShiftsView)] = Permissions.CatalogsView,
            // La pantalla de dispositivos también es la de descargas (en la web, Download.razor); administrar es aparte.
            [nameof(Views.Devices.DevicesView)] = Permissions.DevicesDownload,
            [nameof(Views.Settings.SettingsView)] = Permissions.SettingsManage,
            [nameof(Views.Backup.BackupView)] = Permissions.BackupsManage,
        };

        /// <summary>Permiso de la pantalla; null si basta con haber iniciado sesión (inicio).</summary>
        public static string? For(string viewName) => Required.GetValueOrDefault(viewName);

        public static bool CanOpen(this IAuthenticationStateService auth, string viewName) =>
            auth.IsAuthenticated && (For(viewName) is not { } permission || auth.HasPermission(permission));
    }
}
