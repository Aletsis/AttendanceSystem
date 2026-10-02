namespace AttendanceSystem.Domain.Enumerations;

/// <summary>
/// Representa una opción de rol / privilegio disponible en un reloj checador.
/// </summary>
public sealed record DevicePrivilegeOption(
    DevicePrivilege Value,
    string DisplayName,
    string Description,
    int ProtocolValue);

/// <summary>
/// Gestiona y mapea los privilegios y roles de usuarios según la marca, modelo y forma de comunicación del checador.
/// </summary>
public static class DevicePrivilegeMapper
{
    /// <summary>
    /// Retorna los roles/privilegios admitidos para una marca y método de comunicación específico.
    /// </summary>
    public static IReadOnlyList<DevicePrivilegeOption> GetSupportedPrivileges(
        DeviceBrand brand,
        DeviceDownloadMethod downloadMethod = DeviceDownloadMethod.Sdk)
    {
        return brand switch
        {
            DeviceBrand.Hikvision => new List<DevicePrivilegeOption>
            {
                new(DevicePrivilege.User, "Usuario Normal", "Registro de asistencia estándar. Sin acceso a menú local de configuración.", 0),
                new(DevicePrivilege.Admin, "Administrador", "Acceso total al menú de configuración en la pantalla táctil del terminal.", 2)
            },
            DeviceBrand.ZKTeco => downloadMethod == DeviceDownloadMethod.Adms
                ? new List<DevicePrivilegeOption>
                {
                    new(DevicePrivilege.User, "Usuario Normal", "Registro de asistencia estándar. Sin acceso a menú de administración.", 0),
                    new(DevicePrivilege.Registrar, "Registrador", "Permiso exclusivo para enrolar y gestionar huellas, tarjetas o rostros.", 1),
                    new(DevicePrivilege.Admin, "Administrador", "Gestión de usuarios y configuraciones básicas del sistema.", 2),
                    new(DevicePrivilege.SuperAdmin, "Super Administrador", "Acceso total sin restricciones a menús, red y ajustes del checador.", 14)
                }
                : new List<DevicePrivilegeOption>
                {
                    new(DevicePrivilege.User, "Usuario Normal", "Registro de asistencia estándar. Sin acceso a menú de administración.", 0),
                    new(DevicePrivilege.Registrar, "Registrador", "Permiso exclusivo para enrolar y gestionar huellas, tarjetas o rostros.", 1),
                    new(DevicePrivilege.Admin, "Administrador", "Gestión de usuarios y configuraciones básicas del sistema.", 2),
                    new(DevicePrivilege.SuperAdmin, "Super Administrador", "Acceso total sin restricciones a menús, red y ajustes del checador.", 3)
                },
            _ => new List<DevicePrivilegeOption>
            {
                new(DevicePrivilege.User, "Usuario Normal", "Usuario estándar sin privilegios de administración.", 0),
                new(DevicePrivilege.Admin, "Administrador", "Administrador del dispositivo.", 2)
            }
        };
    }

    /// <summary>
    /// Normaliza un privilegio al conjunto admitido por la marca del reloj checador.
    /// Por ejemplo: Hikvision no cuenta con 'Registrador' ni 'SuperAdmin'; SuperAdmin se normaliza a Admin y Registrador a User.
    /// </summary>
    public static DevicePrivilege NormalizeForDevice(DeviceBrand brand, DevicePrivilege privilege)
    {
        if (brand == DeviceBrand.Hikvision)
        {
            return privilege switch
            {
                DevicePrivilege.SuperAdmin => DevicePrivilege.Admin,
                DevicePrivilege.Registrar => DevicePrivilege.User,
                _ => privilege
            };
        }

        return privilege;
    }

    /// <summary>
    /// Mapea un DevicePrivilege del dominio al valor numérico esperado por el protocolo del checador.
    /// </summary>
    public static int MapToProtocolValue(DeviceBrand brand, DeviceDownloadMethod downloadMethod, DevicePrivilege privilege)
    {
        return brand switch
        {
            DeviceBrand.Hikvision => privilege >= DevicePrivilege.Admin ? 2 : 0,
            DeviceBrand.ZKTeco => downloadMethod == DeviceDownloadMethod.Adms
                ? privilege switch
                {
                    DevicePrivilege.SuperAdmin => 14,
                    DevicePrivilege.Admin => 2,
                    DevicePrivilege.Registrar => 1,
                    _ => 0
                }
                : (int)privilege,
            _ => (int)privilege
        };
    }

    /// <summary>
    /// Mapea el código numérico recibido desde el checador a la enumeración de dominio DevicePrivilege.
    /// </summary>
    public static DevicePrivilege MapFromProtocolValue(DeviceBrand brand, DeviceDownloadMethod downloadMethod, int protocolValue)
    {
        return brand switch
        {
            DeviceBrand.Hikvision => protocolValue >= 2 ? DevicePrivilege.Admin : DevicePrivilege.User,
            DeviceBrand.ZKTeco => downloadMethod == DeviceDownloadMethod.Adms
                ? protocolValue switch
                {
                    14 or 3 => DevicePrivilege.SuperAdmin,
                    2 => DevicePrivilege.Admin,
                    1 => DevicePrivilege.Registrar,
                    _ => DevicePrivilege.User
                }
                : protocolValue switch
                {
                    3 or 14 => DevicePrivilege.SuperAdmin,
                    2 => DevicePrivilege.Admin,
                    1 => DevicePrivilege.Registrar,
                    _ => DevicePrivilege.User
                },
            _ => protocolValue >= 2 ? DevicePrivilege.Admin : DevicePrivilege.User
        };
    }

    /// <summary>
    /// Mapea un DevicePrivilege a la cadena userType de ISAPI para Hikvision ("admin" o "normal").
    /// </summary>
    public static string MapToHikvisionUserType(DevicePrivilege privilege)
    {
        return privilege >= DevicePrivilege.Admin ? "admin" : "normal";
    }

    /// <summary>
    /// Mapea la cadena userType de ISAPI de Hikvision ("admin" / "normal") a DevicePrivilege.
    /// </summary>
    public static DevicePrivilege MapFromHikvisionUserType(string? userType)
    {
        return string.Equals(userType, "admin", StringComparison.OrdinalIgnoreCase)
            ? DevicePrivilege.Admin
            : DevicePrivilege.User;
    }

    /// <summary>
    /// Retorna el nombre de visualización según la marca y método del reloj.
    /// </summary>
    public static string GetDisplayName(
        DeviceBrand brand,
        DevicePrivilege privilege,
        DeviceDownloadMethod downloadMethod = DeviceDownloadMethod.Sdk)
    {
        var supported = GetSupportedPrivileges(brand, downloadMethod);
        var normalized = NormalizeForDevice(brand, privilege);
        var match = supported.FirstOrDefault(o => o.Value == normalized);
        return match?.DisplayName ?? normalized.ToString();
    }
}
