using Microsoft.Extensions.Logging;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.ZKTeco.Adapters;

// Esta es la implementación del puerto IZKTecoDeviceClient
// Vive en Infrastructure pero se compila como x86
public class ZKTecoDeviceClient : IZKTecoDeviceClient
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
                    
                // Diagnóstico: Obtener versión de algoritmo de huella
                try 
                {
                    string zkFpVersion = "";
                    if (_device.GetSysOption(1, "~ZKFPVersion", out zkFpVersion))
                    {
                        _logger.LogInformation("Dispositivo usa algoritmo de huella versión: {ZKFPVersion}", zkFpVersion);
                    }
                }
                catch { /* Ignorar error al obtener versión */ }
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

                        if (fromDate.HasValue && checkTime < fromDate.Value)
                            continue;

                        if (toDate.HasValue && checkTime > toDate.Value)
                            continue;

                        records.Add(new RawAttendanceRecord(
                            UserId: userId,
                            CheckTime: checkTime,
                            VerifyMethod: verifyMode,
                            InOutMode: inOutMode,
                            WorkCode: workCode));
                        addedCount++;
                    }
                    _logger.LogInformation("Lectura completada. Leídos: {TotalRead}, Agregados: {AddedCount}. Rango en Dispositivo: {Min} - {Max}. Filtros: From={From}, To={To}", 
                        totalRead, addedCount, minDeviceDate, maxDeviceDate, fromDate, toDate);
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
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Dispositivo no conectado");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => _device.ClearGLog(1), cancellationToken);
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
                if (_device.GetDeviceStatus(1, 2, ref value)) userCount = value;
                if (_device.GetDeviceStatus(1, 3, ref value)) fingerprintCount = value;
                if (_device.GetDeviceStatus(1, 21, ref value)) faceCount = value;
                if (_device.GetDeviceStatus(1, 6, ref value)) recordCount = value;

                // --- OBTENER CAPACIDADES (GetSysOption) ---
                string sValue = "";
                if (_device.GetSysOption(1, "~MaxUserCount", out sValue) && int.TryParse(sValue, out int uc)) userCapacity = uc;
                if (_device.GetSysOption(1, "~MaxFingerCount", out sValue) && int.TryParse(sValue, out int fc)) fingerprintCapacity = fc;
                if (_device.GetSysOption(1, "~MaxFaceCount", out sValue) && int.TryParse(sValue, out int fac)) faceCapacity = fac;
                if (_device.GetSysOption(1, "~MaxAttLogCount", out sValue) && int.TryParse(sValue, out int rc)) recordCapacity = rc;

                if (userCapacity <= userCount)
                {
                    int maxUser = 0;
                    if (_device.GetDeviceStatus(1, 26, ref maxUser) && maxUser > userCapacity) userCapacity = maxUser;
                }

                if (recordCapacity <= recordCount)
                {
                     int maxLog = 0;
                     if (_device.GetDeviceStatus(1, 29, ref maxLog) && maxLog > recordCapacity) recordCapacity = maxLog;
                }
                
                if (userCapacity < userCount) userCapacity = userCount;
                if (fingerprintCapacity < fingerprintCount) fingerprintCapacity = fingerprintCount;
                if (recordCapacity < recordCount) recordCapacity = recordCount;

                _logger.LogInformation(
                    "Stats: Users={UserCount}/{UserCapacity}, FP={FingerprintCount}/{FingerprintCapacity}, Recs={RecordCount}/{RecordCapacity}",
                    userCount, userCapacity, fingerprintCount, fingerprintCapacity, recordCount, recordCapacity);

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

                string faceTemplate = "";
                int faceLen = 0;
                if (_device.GetUserFaceStr(1, enrollNumber, 50, ref faceTemplate, ref faceLen))
                {
                }
                
                users.Add(new DeviceUserDto(
                    enrollNumber,
                    name,
                    password,
                    privilege,
                    enabled,
                    string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber,
                    fingerprints.Any() ? fingerprints : null,
                    string.IsNullOrWhiteSpace(faceTemplate) ? null : faceTemplate));
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
            return _device.SSR_DeleteEnrollData(1, userId, 12);
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
            bool anyDeleted = false;
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (_device.SSR_DeleteEnrollData(1, userId, i))
                    {
                        anyDeleted = true;
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
            _device.EnableDevice(1, false); // Deshabilitar para evitar conflictos
            try
            {
                // 1. Información Básica y Tarjeta
                if (!string.IsNullOrWhiteSpace(user.CardNumber))
                {
                    _device.SetStrCardNumber(user.CardNumber);
                }
                
                bool result = _device.SSR_SetUserInfo(1, user.UserId, user.Name, user.Password, user.Privilege, user.Enabled);
                
                if (!result)
                {
                    int errorCode = 0;
                    _device.GetLastError(ref errorCode);
                    _logger.LogWarning("Fallo al enviar usuario {UserId} (Info Básica). Código error: {ErrorCode}", user.UserId, errorCode);
                    return false;
                }

                // 3. Huellas
                if (user.Fingerprints != null && user.Fingerprints.Any())
                {
                    foreach (var fp in user.Fingerprints)
                    {
                        // Intento 1: SSR_SetUserTmpStr (Estándar TFT)
                        if (!_device.SSR_SetUserTmpStr(1, user.UserId, fp.Index, fp.Template))
                        {
                            // Fallback 1: SetUserTmpExStr (Soporte VX10 explícito, flag 1 = Valid)
                            // Nota: Algunos SDK/Dispositivos requieren esto para VX10 strings.
                            bool fallbackSuccess = false;
                            try 
                            { 
                                fallbackSuccess = _device.SetUserTmpExStr(1, user.UserId, fp.Index, 1, fp.Template); 
                            } 
                            catch { /* Método no existente en DLL antigua */ }

                            if (!fallbackSuccess)
                            {
                                // Fallback 2: SetUserTmpStr (Legacy B&W)
                                try 
                                { 
                                    if (int.TryParse(user.UserId, out int userIdInt))
                                    {
                                        fallbackSuccess = _device.SetUserTmpStr(1, userIdInt, fp.Index, fp.Template); 
                                    }
                                } 
                                catch { }
                            }

                            if (!fallbackSuccess)
                            {
                                int fpErrorCode = 0;
                                _device.GetLastError(ref fpErrorCode);
                                _logger.LogWarning("Fallo al guardar huella {Index} para {UserId} tras múltiples intentos. Error: {ErrorCode}. Posible incompatibilidad de algoritmo o duplicado.", fp.Index, user.UserId, fpErrorCode);
                            }
                            else
                            {
                                _logger.LogInformation("Huella {Index} para {UserId} guardada usando método alternativo.", fp.Index, user.UserId);
                            }
                        }
                    }
                }

                // 4. Rostro
                if (!string.IsNullOrWhiteSpace(user.FaceTemplate))
                {
                    _device.SetUserFaceStr(1, user.UserId, 50, user.FaceTemplate, user.FaceTemplate.Length);
                }

                _device.RefreshData(1); // Confirmar cambios
                _logger.LogInformation("Usuario y biometría {UserId} enviado exitosamente.", user.UserId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al enviar usuario {UserId}", user.UserId);
                return false;
            }
            finally
            {
                _device.EnableDevice(1, true); // Rehabilitar
            }
        }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}

