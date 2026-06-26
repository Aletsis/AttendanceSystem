using Microsoft.Extensions.Logging;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.ZKTeco.Adapters;

// Esta es la implementación del puerto IDeviceClient
// Vive en Infrastructure pero se compila como x86
public class ZKTecoDeviceClient : IDeviceClient
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
    public async Task<IReadOnlyList<RawAttendanceRecord>> GetAttendanceLogsAsync(
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
            await Task.Run(() =>
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

                }
                else
                {
                    int errorCode = 0;
                    _device.GetLastError(ref errorCode);
                    _logger.LogWarning("ReadAllGLogData devolvió false. Código de error: {ErrorCode}", errorCode);
                }
            }
            finally
            {
                _device.EnableDevice(1, true);
            }
        }, cancellationToken);

        return records;
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

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                if (fromDate.HasValue || toDate.HasValue)
                {
                    // Si falta una de las fechas, tomamos un valor extremo para cubrir el rango completo
                    var start = fromDate ?? new DateTime(2000, 1, 1);
                    var end = toDate ?? DateTime.Now.AddYears(1);

                    var startStr = start.ToString("yyyy-MM-dd HH:mm:ss");
                    var endStr = end.ToString("yyyy-MM-dd HH:mm:ss");

                    _logger.LogInformation("Borrando logs del dispositivo ZKTeco en el rango: {Start} - {End}", startStr, endStr);

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
                }
                else
                {
                    _logger.LogInformation("Borrando todos los logs del dispositivo ZKTeco (ClearGLog)...");
                    bool success = _device.ClearGLog(1);
                    if (success)
                    {
                        _device.RefreshData(1);
                    }
                    return success;
                }
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

                // --- OBTENER CONTEOS ACTUALES (GetDeviceStatus) ---
                int value = 0;
                if (_device.GetDeviceStatus(1, 2, ref value)) userCount = value;           // 2: Usuarios
                if (_device.GetDeviceStatus(1, 3, ref value)) fingerprintCount = value;    // 3: Huellas
                if (_device.GetDeviceStatus(1, 21, ref value)) faceCount = value;          // 21: Rostros
                if (_device.GetDeviceStatus(1, 6, ref value)) recordCount = value;         // 6: Registros (SSR)
                if (recordCount == 0 && _device.GetDeviceStatus(1, 1, ref value)) recordCount = value; // Fallback 1: Registros (General)

                // --- OBTENER CAPACIDADES ---
                
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

                // Intentar leer múltiples opciones y quedarse con la que parezca una capacidad válida (generalmente números redondos o mayores a 1000)
                int[] sysUserOptions = { TryGetSysOption("~MaxUserCount"), TryGetSysOption("MaxUser"), TryGetSysOption("MaxUserCapacity") };
                int[] sysFingerOptions = { TryGetSysOption("~MaxFingerCount"), TryGetSysOption("MaxFinger"), TryGetSysOption("MaxFingerCapacity") };
                int[] sysFaceOptions = { TryGetSysOption("~MaxFaceCount"), TryGetSysOption("MaxFace"), TryGetSysOption("MaxFaceCapacity") };
                int[] sysRecordOptions = { TryGetSysOption("~MaxAttLogCount"), TryGetSysOption("MaxAttLog"), TryGetSysOption("MaxAttLogCapacity") };

                userCapacity = sysUserOptions.Max();
                fingerprintCapacity = sysFingerOptions.Max();
                faceCapacity = sysFaceOptions.Max();
                recordCapacity = sysRecordOptions.Max();

                // Intento 2: GetDeviceStatus (7, 8, 9, 10, 22)
                int dsUserCap = TryGetDeviceStatus(8);
                int dsFingerCap = TryGetDeviceStatus(7);
                int dsRecordCap = TryGetDeviceStatus(9);
                int dsFaceCap = TryGetDeviceStatus(10);
                int dsFaceCap2 = TryGetDeviceStatus(22);

                // Si GetDeviceStatus nos da exactamente el mismo número que el count, ES UN BUG DEL FIRMWARE (está devolviendo el count en lugar de capacity).
                // Ignorar capacidades menores a 1000 (excepto rostros) o que sean exactamente igual al conteo (falso positivo).
                if (userCapacity <= 0 && dsUserCap > 0 && dsUserCap != userCount) userCapacity = dsUserCap;
                if (fingerprintCapacity <= 0 && dsFingerCap > 0 && dsFingerCap != fingerprintCount) fingerprintCapacity = dsFingerCap;
                if (recordCapacity <= 0 && dsRecordCap > 0 && dsRecordCap != recordCount) recordCapacity = dsRecordCap;
                
                if (faceCapacity <= 0)
                {
                    if (dsFaceCap > 0 && dsFaceCap != faceCount) faceCapacity = dsFaceCap;
                    else if (dsFaceCap2 > 0 && dsFaceCap2 != faceCount) faceCapacity = dsFaceCap2;
                }

                // Ajuste final de seguridad: Si todo falló o dio el conteo exacto, poner capacidades por defecto estándar de ZKTeco
                if (userCapacity <= userCount) userCapacity = Math.Max(userCount > 3000 ? 10000 : 3000, userCount); 
                if (fingerprintCapacity <= fingerprintCount) fingerprintCapacity = Math.Max(fingerprintCount > 3000 ? 10000 : 3000, fingerprintCount);
                if (faceCapacity <= faceCount && faceCount > 0) faceCapacity = Math.Max(faceCount > 1500 ? 3000 : 1500, faceCount);
                if (recordCapacity <= recordCount) recordCapacity = Math.Max(recordCount > 50000 ? 100000 : 50000, recordCount);

                _logger.LogInformation(
                    "Estadísticas del Dispositivo: Usuarios={UserCount}/{UserCapacity}, Huellas={FingerprintCount}/{FingerprintCapacity}, Rostros={FaceCount}/{FaceCapacity}, Registros={RecordCount}/{RecordCapacity}",
                    userCount, userCapacity, fingerprintCount, fingerprintCapacity, faceCount, faceCapacity, recordCount, recordCapacity);

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
                    recordCapacity
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
}

