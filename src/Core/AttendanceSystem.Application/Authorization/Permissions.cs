using System.Reflection;

namespace AttendanceSystem.Application.Authorization;

/// <summary>Roles del sistema (caso 13). Hoy son fijos; cada uno es un paquete de permisos (<see cref="RolePermissions"/>).</summary>
public static class AppRoles
{
    public const string Administrator = "Administrador";
    public const string HumanResources = "RH";
    public const string User = "Usuario";

    public static readonly IReadOnlyList<string> All = new[] { Administrator, HumanResources, User };
}

/// <summary>
/// Caso 13: catálogo de permisos. Las páginas, botones y comandos piden un permiso, nunca un rol; así los roles
/// pueden volverse configurables (guardados en la base de datos) sin tocar la interfaz ni los comandos.
/// </summary>
public static class Permissions
{
    // Empleados
    public const string EmployeesView = "Employees.View";
    public const string EmployeesCreate = "Employees.Create";
    public const string EmployeesEdit = "Employees.Edit";
    /// <summary>Tipo de turno, horario, esquema y días de descanso (también con vigencia pasada).</summary>
    public const string EmployeesEditSchedule = "Employees.EditSchedule";
    /// <summary>Autorización, redondeo, topes y extras antes de la entrada.</summary>
    public const string EmployeesEditOvertime = "Employees.EditOvertime";
    /// <summary>Caso 16: vacaciones, incapacidad y baja temporal (registrar, cambiar o terminar el período).</summary>
    public const string EmployeesLeave = "Employees.Leave";
    public const string EmployeesDelete = "Employees.Delete";
    public const string EmployeesImport = "Employees.Import";
    public const string EmployeesExport = "Employees.Export";
    /// <summary>Privilegio del reloj (usuario o administrador); sin él, el empleado entra como usuario normal.</summary>
    public const string EmployeesDevicePrivilege = "Employees.DevicePrivilege";
    public const string EmployeesSendToDevice = "Employees.SendToDevice";

    public const string ExternalEmployeesCreate = "ExternalEmployees.Create";
    public const string ExternalEmployeesEdit = "ExternalEmployees.Edit";
    public const string ExternalEmployeesDelete = "ExternalEmployees.Delete";

    // Catálogos
    public const string CatalogsView = "Catalogs.View";
    public const string BranchesManage = "Branches.Manage";
    public const string DepartmentsManage = "Departments.Manage";
    public const string PositionsManage = "Positions.Manage";
    public const string PositionsDelete = "Positions.Delete";
    public const string ShiftsManage = "Shifts.Manage";
    public const string RosterManage = "Roster.Manage";

    // Dispositivos y descargas
    public const string DevicesManage = "Devices.Manage";
    public const string DevicesDownload = "Devices.Download";
    public const string DevicesDownloadAll = "Devices.DownloadAll";
    public const string DevicesForceFullDownload = "Devices.ForceFullDownload";
    public const string ClockAlertsView = "ClockAlerts.View";
    public const string ClockAlertsResolve = "ClockAlerts.Resolve";

    // Asistencia
    public const string AttendanceView = "Attendance.View";
    public const string AttendanceCalculate = "Attendance.Calculate";
    /// <summary>Asignar/desasignar marcajes, turno y descanso del día, salidas temporales y extras.</summary>
    public const string AttendanceAdjust = "Attendance.Adjust";
    public const string AttendanceManualPunch = "Attendance.ManualPunch";
    public const string AttendanceVoidManualPunch = "Attendance.VoidManualPunch";
    public const string AttendanceViewHistory = "Attendance.ViewHistory";

    // Reportes
    public const string ReportsView = "Reports.View";
    public const string ReportsPayrollExport = "Reports.PayrollExport";

    // Sistema
    public const string SettingsManage = "Settings.Manage";
    public const string BackupsManage = "Backups.Manage";
    public const string UsersManage = "Users.Manage";
    public const string JobsView = "Jobs.View";

    public static readonly IReadOnlyList<string> All = typeof(Permissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}

/// <summary>
/// Permisos de cada rol (matriz acordada en el caso 13). Administrador tiene todos. Para roles configurables basta
/// con reemplazar esta tabla por una leída de la base de datos detrás de <see cref="IPermissionService"/>.
/// </summary>
public static class RolePermissions
{
    private static readonly string[] UserPermissions =
    {
        Permissions.EmployeesView,
        Permissions.EmployeesCreate,
        Permissions.EmployeesEdit,
        Permissions.EmployeesImport,
        Permissions.EmployeesExport,
        Permissions.EmployeesSendToDevice,
        Permissions.ExternalEmployeesCreate,
        Permissions.CatalogsView,
        Permissions.DevicesDownload,
        Permissions.ClockAlertsView,
        Permissions.AttendanceView,
        Permissions.ReportsView,
    };

    private static readonly string[] HumanResourcesPermissions = UserPermissions.Concat(new[]
    {
        Permissions.EmployeesEditSchedule,
        Permissions.EmployeesEditOvertime,
        Permissions.EmployeesLeave,
        Permissions.ExternalEmployeesEdit,
        Permissions.PositionsManage,
        Permissions.RosterManage,
        Permissions.AttendanceCalculate,
        Permissions.AttendanceAdjust,
        Permissions.AttendanceManualPunch,
        Permissions.AttendanceViewHistory,
        Permissions.ReportsPayrollExport,
    }).ToArray();

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByRole =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [AppRoles.Administrator] = Permissions.All.ToHashSet(),
            [AppRoles.HumanResources] = HumanResourcesPermissions.ToHashSet(),
            [AppRoles.User] = UserPermissions.ToHashSet(),
        };

    public static IReadOnlySet<string> Of(string role) =>
        ByRole.TryGetValue(role, out var permissions) ? permissions : new HashSet<string>();

    public static bool Grants(IEnumerable<string> roles, string permission) =>
        roles.Any(role => Of(role).Contains(permission));
}
