using Microsoft.Extensions.Logging;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.ZKTeco.Adapters;

// Esta es la implementación del puerto IDeviceClient
// Vive en Infrastructure pero se compila como x86
public class ZKTecoDeviceClient : IDeviceClient, IDisposable
{
    private readonly zkemkeeper.CZKEMClass _device;
    private readonly ILogger<ZKTecoDeviceClient> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isConnected;

    public ZKTecoDeviceClient(ILogger<ZKTecoDeviceClient> logger)
    {
        try
        {
            _device = new zkemkeeper.CZKEMClass();
            _logger = logger;
            _logger.LogInformation("ZKTeco SDK inicializado correctamente");
        }
        catch (Exception ex)
        {
            _logger = logger;
            _logger.LogError(ex, "Error al inicializar el SDK de ZKTeco. Asegúrese de que zkemkeeper.dll esté registrado correctamente.");
            throw new InvalidOperationException(
                "No se pudo inicializar el SDK de ZKTeco. " +
                "Ejecute: regsvr32 /s zkemkeeper.dll desde la carpeta del SDK como administrador.", ex);
        }
    }

    public async Task<bool> ConnectAsync(
        string ipAddress,
        int port,
        string? username = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
        {
            _isConnected = _device.Connect_Net(ipAddress, port);

            if (_isConnected)
            {
                _logger.LogInformation(
                    "Conectado exitosamente a {IpAddress}:{Port}", ipAddress, port);

                // Diagnóstico: Obtener versión de algoritmo de huella y forzar modo Unicode si es posible
                try
                {
                    string zkFpVersion = "";
                    if (_device.GetSysOption(1, "~ZKFPVersion", out zkFpVersion))
                    {
                        _logger.LogInformation("Dispositivo usa algoritmo de huella versión: {ZKFPVersion}", zkFpVersion);
                    }
                }
                catch { /* Ignorar error */ }
            }
            else
            {
                int errorCode = 0;
                _device.GetLastError(ref errorCode);
                _logger.LogError(
                    "Error al conectar a {IpAddress}:{Port}. Código: {ErrorCode}",
                    ipAddress, port, errorCode);
            }

            return _isConnected;
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ... (Métodos intermedios sin cambios hasta SetUserAsync) ...
    public async Task<DeviceLogsResult> GetAttendanceLogsAsync(
        string deviceId,
        DateTime? fromDate,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var records = new List<RawAttendanceRecord>();
            var readResult = await Task.Run<DeviceLogsResult>(() =>
        {
            _device.EnableDevice(1, false);

            try
            {
                // El SDK de ZKTeco devuelve fechas en hora LOCAL del dispositivo.
                // fromDate/toDate llegan en UTC desde la capa de aplicación.
                // Convertimos a hora local antes de filtrar para evitar descartar
                // registros válidos por el desfase de zona horaria.
                var localFromDate = fromDate.HasValue
                    ? fromDate.Value.Kind == DateTimeKind.Utc
                        ? fromDate.Value.ToLocalTime()
                        : fromDate.Value
                    : (DateTime?)null;

                var localToDate = toDate.HasValue
                    ? toDate.Value.Kind == DateTimeKind.Utc
                        ? toDate.Value.ToLocalTime()
                        : toDate.Value
                    : (DateTime?)null;

                _logger.LogInformation("Leyendo datos del dispositivo (ReadAllGLogData)...");
                if (_device.ReadAllGLogData(1))
                {
                    string userId;
                    int verifyMode, inOutMode, year, month, day, hour, minute, second;
                    int workCode = 0;
                    int totalRead = 0;
                    int addedCount = 0;
                    DateTime? minDeviceDate = null;
                    DateTime? maxDeviceDate = null;

                    while (_device.SSR_GetGeneralLogData(
                        1, out userId, out verifyMode, out inOutMode,
                        out year, out month, out day, out hour, out minute, out second,
                        ref workCode))
                    {
                        totalRead++;
                        var checkTime = new DateTime(
                            year, month, day,
                            hour, minute, second);

                        if (minDeviceDate == null || checkTime < minDeviceDate) minDeviceDate = checkTime;
                        if (maxDeviceDate == null || checkTime > maxDeviceDate) maxDeviceDate = checkTime;

                        if (localFromDate.HasValue && checkTime < localFromDate.Value)
                            continue;

                        if (localToDate.HasValue && checkTime > localToDate.Value)
                            continue;

                        records.Add(new RawAttendanceRecord(
                            UserId: userId,
                            CheckTime: checkTime,
                            VerifyMethod: verifyMode,
                            InOutMode: inOutMode,
                            WorkCode: workCode));
                        addedCount++;
                    }
                    _logger.LogInformation("Lectura completada. Leídos: {TotalRead}, Agregados: {AddedCount}. Rango en Dispositivo: {Min} - {Max}. Filtros (hora local): From={From}, To={To}",
                        totalRead, addedCount, minDeviceDate, maxDeviceDate, localFromDate, localToDate);

                    return records.Count == 0 ? DeviceLogsResult.Empty(totalRead) : DeviceLogsResult.Success(records, totalRead);
                }
                else
                {
                    int errorCode = 0;
                    _device.GetLastError(ref errorCode);
                    _logger.LogError("ReadAllGLogData devolvió false. Código de error SDK: {ErrorCode}", errorCode);
                    return DeviceLogsResult.Failed($"Fallo al leer datos del dispositivo ZKTeco (SDK Error Code: {errorCode})");
                }
            }
            finally
            {
                _device.EnableDevice(1, true);
            }
        }, cancellationToken);

            return readResult;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ClearLogsAsync(
        string deviceId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Dispositivo no conectado");

        if (!fromDate.HasValue && !toDate.HasValue)
        {
            _logger.LogError("Intento de borrado no acotado (ClearGLog) bloqueado por seguridad en ZKTeco para dispositivo {DeviceId}.", deviceId);
            throw new ArgumentException("La limpieza de marcajes requiere fechas límite confirmadas (fromDate y toDate) para evitar borrado total accidental.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                var start = fromDate ?? new DateTime(2000, 1, 1);
                var end = toDate ?? DateTime.UtcNow;

                var startStr = start.ToString("yyyy-MM-dd HH:mm:ss");
                var endStr = end.ToString("yyyy-MM-dd HH:mm:ss");

                _logger.LogInformation("Borrando logs del dispositivo ZKTeco en el rango confirmado: {Start} - {End}", startStr, endStr);

                bool success = _device.DeleteAttlogBetweenTheDate(1, startStr, endStr);
                if (success)
                {
                    _device.RefreshData(1);
                }
                else
                {
                    int errorCode = 0;
                    _device.GetLastError(ref errorCode);
                    _logger.LogError("Fallo al borrar logs por rango en el dispositivo. SDK Error Code: {ErrorCode}", errorCode);
                }
                return success;
            }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
        {
            if (_isConnected)
            {
                _device.Disconnect();
                _isConnected = false;
            }
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
        {
            try
            {
                // Obtener número de serie
                string serialNumber = string.Empty;
                _device.GetSerialNumber(1, out serialNumber);

                // Obtener versión de firmware
                string firmwareVersion = string.Empty;
                _device.GetFirmwareVersion(1, ref firmwareVersion);

                // Obtener plataforma
                string platform = string.Empty;
                _device.GetPlatform(1, ref platform);

                _logger.LogInformation(
                    "Información del dispositivo obtenida: S/N={SerialNumber}, FW={FirmwareVersion}",
                    serialNumber, firmwareVersion);

                string deviceName = string.Empty;

                int userCount = 0, fingerprintCount = 0, faceCount = 0, recordCount = 0;
                int userCapacity = 0, fingerprintCapacity = 0, faceCapacity = 0, recordCapacity = 0;

                // Helper para limpiar strings que vienen de COM interop (con null terminators)
                bool TryParseClean(string val, out int result)
                {
                    result = 0;
                    if (string.IsNullOrWhiteSpace(val)) return false;
                    string cleaned = val.Replace("\0", "").Trim();
                    return int.TryParse(cleaned, out result);
                }

                int TryGetSysOption(string key)
                {
                    string sValue = "";
                    bool success = _device.GetSysOption(1, key, out sValue);

                    // Mostrar el dato TOTALMENTE EN CRUDO
                    _logger.LogInformation("RAW GetSysOption [{Key}] -> Success: {Success}, RawValue: '{RawValue}'", key, success, sValue);

                    if (success && TryParseClean(sValue, out int result))
                    {
                        return result;
                    }
                    return 0;
                }

                int TryGetDeviceStatus(int code)
                {
                    int temp = 0;
                    bool success = _device.GetDeviceStatus(1, code, ref temp);

                    // Mostrar el dato TOTALMENTE EN CRUDO
                    _logger.LogInformation("RAW GetDeviceStatus [{Code}] -> Success: {Success}, RawValue: {RawValue}", code, success, temp);

                    if (success)
                    {
                        return temp;
                    }
                    return 0;
                }

                // --- OBTENER CONTEOS ACTUALES (GetDeviceStatus) ---
                userCount = TryGetDeviceStatus(2);           // 2: Usuarios
                fingerprintCount = TryGetDeviceStatus(3);    // 3: Huellas
                faceCount = TryGetDeviceStatus(21);          // 21: Rostros

                // Status 6 es el estándar de registros de asistencia en terminales TFT/Faciales.
                // Status 1 en terminales TFT es el número de administradores, y en modelos B&W antiguos es registros generales.
                int dsRecord6 = TryGetDeviceStatus(6);       // 6: Registros de asistencia (AttLog)
                int dsRecord1 = TryGetDeviceStatus(1);       // 1: Admins (TFT) o Registros (General/Legacy)

                recordCount = dsRecord6 > 0 ? dsRecord6 : dsRecord1;

                // --- OBTENER CAPACIDADES ---
                // Prioridad 1: GetDeviceStatus es la API nativa autoritativa de ZKTeco para capacidades exactas:
                // 8: Capacidad de Usuarios (Max User Count)
                // 7: Capacidad de Huellas (Max Fingerprint Count)
                // 9: Capacidad de Registros de Asistencia (Max AttLog Count)
                // 22: Capacidad de Rostros (Max Face Count) - OJO: Código 10 es Admin/OPLog, NO rostros.
                int dsUserCap = TryGetDeviceStatus(8);
                int dsFingerCap = TryGetDeviceStatus(7);
                int dsRecordCap = TryGetDeviceStatus(9);
                int dsFaceCap = TryGetDeviceStatus(22);

                userCapacity = dsUserCap;
                fingerprintCapacity = dsFingerCap;
                recordCapacity = dsRecordCap;
                faceCapacity = dsFaceCap;

                // Prioridad 2: Fallback con GetSysOption si GetDeviceStatus no devolvió valor (> 0)
                // Importante: En firmwares ZKTeco, los valores de SysOption (~MaxUserCount, ~MaxAttLogCount, etc.)
                // suelen venir expresados en factores de escala (centenas x100 o decenas de miles x10,000).
                if (userCapacity <= 0)
                {
                    int[] sysUserOptions = { TryGetSysOption("~MaxUserCount"), TryGetSysOption("MaxUser"), TryGetSysOption("MaxUserCapacity") };
                    int sysUser = sysUserOptions.Max();
                    if (sysUser > 0)
                    {
                        userCapacity = (sysUser <= 100) ? sysUser * 100 : sysUser;
                    }
                }

                if (recordCapacity <= 0)
                {
                    int[] sysRecordOptions = { TryGetSysOption("~MaxAttLogCount"), TryGetSysOption("MaxAttLog"), TryGetSysOption("MaxAttLogCapacity") };
                    int sysRecord = sysRecordOptions.Max();
                    if (sysRecord > 0)
                    {
                        recordCapacity = (sysRecord <= 100) ? sysRecord * 10000 : sysRecord;
                    }
                }

                if (faceCapacity <= 0)
                {
                    int[] sysFaceOptions = { TryGetSysOption("~MaxFaceCount"), TryGetSysOption("MaxFace"), TryGetSysOption("MaxFaceCapacity") };
                    int sysFace = sysFaceOptions.Max();
                    if (sysFace > 0)
                    {
                        faceCapacity = (sysFace <= 100) ? sysFace * 100 : sysFace;
                    }
                }

                if (fingerprintCapacity <= 0)
                {
                    // Si el dispositivo tiene huellas enroladas pero GetDeviceStatus(7) dio 0, consultar GetSysOption
                    if (fingerprintCount > 0)
                    {
                        int[] sysFingerOptions = { TryGetSysOption("~MaxFingerCount"), TryGetSysOption("MaxFinger"), TryGetSysOption("MaxFingerCapacity") };
                        int sysFinger = sysFingerOptions.Max();
                        if (sysFinger > 0)
                        {
                            fingerprintCapacity = (sysFinger <= 100) ? sysFinger * 100 : sysFinger;
                        }
                    }
                }

                // Prioridad 3: Ajuste de seguridad final (la capacidad no puede ser inferior al número de elementos enrolados)
                if (userCapacity < userCount) userCapacity = Math.Max(userCount > 3000 ? 10000 : 3000, userCount);
                if (fingerprintCapacity < fingerprintCount) fingerprintCapacity = Math.Max(fingerprintCount > 3000 ? 10000 : 3000, fingerprintCount);
                if (faceCapacity < faceCount && faceCount > 0) faceCapacity = Math.Max(faceCount > 1500 ? 3000 : 1500, faceCount);
                if (recordCapacity < recordCount) recordCapacity = Math.Max(recordCount > 50000 ? 100000 : 50000, recordCount);

                // --- OBTENER VERSIÓN SDK, PUSH Y MAC ---
                string sdkVersion = string.Empty;
                try
                {
                    _device.GetSDKVersion(ref sdkVersion);
                    sdkVersion = sdkVersion?.Replace("\0", "").Trim() ?? string.Empty;
                }
                catch { }
                if (string.IsNullOrWhiteSpace(sdkVersion))
                {
                    string sVal = "";
                    if (_device.GetSysOption(1, "~SDKVersion", out sVal)) sdkVersion = sVal?.Replace("\0", "").Trim() ?? "";
                }

                string pushVersion = string.Empty;
                try
                {
                    string sVal = "";
                    if (_device.GetSysOption(1, "PushVersion", out sVal) && !string.IsNullOrWhiteSpace(sVal))
                        pushVersion = sVal.Replace("\0", "").Trim();
                    else if (_device.GetSysOption(1, "PushProtVer", out sVal) && !string.IsNullOrWhiteSpace(sVal))
                        pushVersion = sVal.Replace("\0", "").Trim();
                    else if (_device.GetSysOption(1, "~PushVersion", out sVal) && !string.IsNullOrWhiteSpace(sVal))
                        pushVersion = sVal.Replace("\0", "").Trim();
                }
                catch { }

                string macAddress = string.Empty;
                try
                {
                    string sVal = "";
                    if (_device.GetSysOption(1, "MAC", out sVal) && !string.IsNullOrWhiteSpace(sVal))
                        macAddress = sVal.Replace("\0", "").Trim();
                    else if (_device.GetSysOption(1, "~MAC", out sVal) && !string.IsNullOrWhiteSpace(sVal))
                        macAddress = sVal.Replace("\0", "").Trim();
                }
                catch { }

                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    string sVal = "";
                    if (_device.GetSysOption(1, "~DeviceName", out sVal) && !string.IsNullOrWhiteSpace(sVal))
                        deviceName = sVal.Replace("\0", "").Trim();
                }

                _logger.LogInformation(
                    "Estadísticas del Dispositivo: Usuarios={UserCount}/{UserCapacity}, Huellas={FingerprintCount}/{FingerprintCapacity}, Rostros={FaceCount}/{FaceCapacity}, Registros={RecordCount}/{RecordCapacity}, SDK={SdkVersion}, Push={PushVersion}, MAC={MacAddress}",
                    userCount, userCapacity, fingerprintCount, fingerprintCapacity, faceCount, faceCapacity, recordCount, recordCapacity, sdkVersion, pushVersion, macAddress);

                return new DeviceInfoDto(
                    serialNumber,
                    deviceName,
                    firmwareVersion,
                    platform,
                    userCount,
                    fingerprintCount,
                    faceCount,
                    recordCount,
                    userCapacity,
                    fingerprintCapacity,
                    faceCapacity,
                    recordCapacity,
                    pushVersion,
                    sdkVersion,
                    macAddress
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener información del dispositivo");
                return null;
            }
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
        {
            var users = new List<DeviceUserDto>();
            _device.ReadAllUserID(1);
            _device.ReadAllTemplate(1);

            string enrollNumber = "";
            string name = "";
            string password = "";
            int privilege = 0;
            bool enabled = false;
            string faceTemplate = "";
            int faceLen = 0;

            while (_device.SSR_GetAllUserInfo(1, out enrollNumber, out name, out password, out privilege, out enabled))
            {
                string cardNumber = "";
                _device.GetStrCardNumber(out cardNumber);

                var fingerprints = new List<DeviceFingerprintDto>();
                for (int i = 0; i < 10; i++)
                {
                    string template = "";
                    int tmpLen = 0;
                    if (_device.SSR_GetUserTmpStr(1, enrollNumber, i, out template, out tmpLen))
                    {
                        fingerprints.Add(new DeviceFingerprintDto(i, template));
                    }
                }

                if (_device.GetUserFaceStr(1, enrollNumber, 50, ref faceTemplate, ref faceLen))
                {
                }

                string photoData = "";
                /*
                if (_device.GetUserPhoto(1, enrollNumber, out photoData))
                {
                    // photoData is Base64
                }
                */

                users.Add(new DeviceUserDto(
                    enrollNumber,
                    name,
                    password,
                    privilege,
                    enabled,
                    string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber,
                    fingerprints.Any() ? fingerprints : null,
                    string.IsNullOrWhiteSpace(faceTemplate) ? null : faceTemplate,
                    Photo: string.IsNullOrWhiteSpace(photoData) ? null : photoData));
            }
            return users;
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                // 0xff (255) deletes the user entirely (fingerprints, face, card, password, and user info).
                return _device.SSR_DeleteEnrollData(1, userId, 0xff);
            }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                // ZKTeco SDK: 12 deletes all fingerprints at once.
                bool success = _device.SSR_DeleteEnrollData(1, userId, 12);
                if (success)
                {
                    _logger.LogInformation("Todas las huellas eliminadas para usuario {UserId} usando backup number 12", userId);
                    return true;
                }

                // Fallback a borrar una por una
                _logger.LogWarning("Fallo al eliminar con backup number 12. Intentando borrar huellas una por una...");
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        if (_device.SSR_DeleteEnrollData(1, userId, i))
                        {
                            _logger.LogInformation("Huella {FingerIndex} eliminada para usuario {UserId}", i, userId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error al intentar eliminar huella {FingerIndex} para {UserId}. Ignorando.", i, userId);
                    }
                    Thread.Sleep(20);
                }
                return true;
            }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
        {
            return _device.ClearData(1, 5);
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
        {
            try
            {
                return _device.SetDeviceTime2(1, dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second);
            }
            catch
            {
                _logger.LogWarning("SetDeviceTime2 no disponible, intentando SetDeviceTime");
                return false;
            }
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> SetUserAsync(DeviceUserDto user, CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                _device.EnableDevice(1, false); // Deshabilitar durante actualización
                try
                {
                    // Determinar si el ID es numérico para fallbacks legacy
                    int dwEnrollNumber = 0;
                    bool isNumericId = int.TryParse(user.UserId, out dwEnrollNumber);

                    // 1. Iniciar modo batch para agrupar todas las operaciones del usuario.
                    // Esto evita escrituras lentas y fragmentadas, mejorando la velocidad de transferencia.
                    _device.BeginBatchUpdate(1, 1);

                    // 2. Tarjeta (CRÍTICO: DEBE establecerse ANTES de llamar a SSR_SetUserInfo o SetUserInfo,
                    // ya que la función de información de usuario asocia la tarjeta que está actualmente en el buffer)
                    _device.SetStrCardNumber(user.CardNumber ?? "");

                    // 3. Información Básica - Limpiar nombre y limitar longitud
                    // ZKTeco suele tener un límite de 24 caracteres para el nombre.
                    string cleanName = CleanName(user.Name);
                    if (cleanName.Length > 24) cleanName = cleanName.Substring(0, 24);

                    // Registrar información de usuario en el buffer
                    bool result = _device.SSR_SetUserInfo(1, user.UserId, cleanName, user.Password, user.Privilege, user.Enabled);

                    // Si falló el método moderno y el ID es numérico, intentar con el método legacy como fallback (para dispositivos B&W antiguos)
                    if (!result && isNumericId)
                    {
                        result = _device.SetUserInfo(1, dwEnrollNumber, cleanName, user.Password, user.Privilege, user.Enabled);
                    }

                    if (!result)
                    {
                        int errorCode = 0;
                        _device.GetLastError(ref errorCode);
                        _logger.LogWarning("Fallo al registrar usuario {UserId} en el buffer del SDK. Código error: {ErrorCode}", user.UserId, errorCode);

                        // Cerrar/cancelar batch para no dejar al SDK en estado intermedio bloqueado
                        _device.BatchUpdate(1);
                        return false;
                    }

                    // 4. Huellas
                    if (user.Fingerprints != null && user.Fingerprints.Any())
                    {
                        foreach (var fp in user.Fingerprints)
                        {
                            // Priorizar SetUserTmpExStr (Mejor para VX10 y dispositivos modernos)
                            // Flag 1 = Huella válida/estándar
                            if (!_device.SetUserTmpExStr(1, user.UserId, fp.Index, 1, fp.Template))
                            {
                                // Fallback a SSR_SetUserTmpStr (Estándar TFT)
                                if (!_device.SSR_SetUserTmpStr(1, user.UserId, fp.Index, fp.Template))
                                {
                                    // Fallback final a SetUserTmpStr (Legacy/B&W) para IDs numéricos
                                    if (isNumericId) _device.SetUserTmpStr(1, dwEnrollNumber, fp.Index, fp.Template);
                                }
                            }
                        }
                    }

                    // 5. Rostro
                    if (!string.IsNullOrWhiteSpace(user.FaceTemplate))
                    {
                        _device.SetUserFaceStr(1, user.UserId, 50, user.FaceTemplate, user.FaceTemplate.Length);
                    }

                    _device.BatchUpdate(1); // Confirmar y volcar todos los cambios (User Info, Card, Templates) al dispositivo en un solo lote
                    _device.RefreshData(1); // Refrescar caché del dispositivo

                    _logger.LogInformation("Usuario {UserId} ({Name}) con tarjeta y biometría enviado exitosamente.", user.UserId, user.Name);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Excepción al enviar usuario {UserId}", user.UserId);
                    try
                    {
                        // Asegurar de cerrar el batch en caso de error inesperado
                        _device.BatchUpdate(1);
                    }
                    catch { /* Ignorar error secundario */ }
                    return false;
                }
                finally
                {
                    _device.EnableDevice(1, true); // Rehabilitar el dispositivo para su uso normal
                }
            }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }


    public async Task<DeviceUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                string name = "", password = "";
                int privilege = 0;
                bool enabled = false;

                if (_device.SSR_GetUserInfo(1, userId, out name, out password, out privilege, out enabled))
                {
                    // Obtener datos adicionales
                    string cardNumber = "";
                    _device.GetStrCardNumber(out cardNumber);

                    var fingerprints = new List<DeviceFingerprintDto>();
                    for (int i = 0; i < 10; i++)
                    {
                        string template = "";
                        int tmpLen = 0;
                        int flag = 0;

                        // Intentar GetUserTmpExStr primero (Soporta 10.0 y es más robusto)
                        if (_device.GetUserTmpExStr(1, userId, i, out flag, out template, out tmpLen))
                        {
                            fingerprints.Add(new DeviceFingerprintDto(i, template));
                        }
                        // Fallback a SSR_GetUserTmpStr
                        else if (_device.SSR_GetUserTmpStr(1, userId, i, out template, out tmpLen))
                        {
                            fingerprints.Add(new DeviceFingerprintDto(i, template));
                        }
                        // Fallback legacy para IDs numéricos
                        else if (int.TryParse(userId, out int dwId))
                        {
                            if (_device.GetUserTmpStr(1, dwId, i, ref template, ref tmpLen))
                            {
                                fingerprints.Add(new DeviceFingerprintDto(i, template));
                            }
                        }
                    }

                    string faceTemplate = "";
                    int faceLen = 0;
                    _device.GetUserFaceStr(1, userId, 50, ref faceTemplate, ref faceLen);

                    return new DeviceUserDto(
                        userId,
                        name,
                        password,
                        privilege,
                        enabled,
                        string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber,
                        fingerprints.Any() ? fingerprints : null,
                        string.IsNullOrWhiteSpace(faceTemplate) ? null : faceTemplate
                    );
                }

                return null;
            }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
    private string CleanName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // Muchos dispositivos ZKTeco no soportan acentos o eñes y muestran basura
        try
        {
            var normalizedString = name.Normalize(System.Text.NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            string clean = stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
            // Reemplazar ñ/Ñ manualmente si quedaron
            return clean.Replace("ñ", "n").Replace("Ñ", "N");
        }
        catch
        {
            return name;
        }
    }

    private int _disposed;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            _lock.Wait(TimeSpan.FromSeconds(3));
            if (_isConnected)
            {
                _device.Disconnect();
                _isConnected = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al desconectar dispositivo durante Dispose");
        }
        finally
        {
            try { _lock.Release(); } catch { }
            if (OperatingSystem.IsWindows() && System.Runtime.InteropServices.Marshal.IsComObject(_device))
            {
                try
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_device);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error liberando objeto COM");
                }
            }
            _lock.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

