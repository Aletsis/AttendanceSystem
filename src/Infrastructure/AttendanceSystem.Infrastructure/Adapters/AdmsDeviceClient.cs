using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.Infrastructure.Adapters;

public class AdmsDeviceClient : IDeviceClient
{
    private readonly IAdmsCommandService _admsCommandService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdmsDeviceClient> _logger;
    private string? _serialNumber;
    private string? _ipAddress;
    private int _port;
    private string? _username;
    private string? _password;

    // Sesión SDK que comparten los envíos de usuarios hasta DisconnectAsync: al mandar muchos usuarios no se reconecta
    // por cada uno, y si el SDK no responde no se espera su timeout (30 s) una vez por usuario antes de ir por ADMS.
    private GrpcZKTecoDeviceClient? _sdkSession;
    private bool _sdkUnavailable;

    public AdmsDeviceClient(IAdmsCommandService admsCommandService, IServiceProvider serviceProvider, ILogger<AdmsDeviceClient> logger)
    {
        _admsCommandService = admsCommandService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    // Relojes de control de acceso (DeviceType=acc) usan las tablas en minúscula del PUSH de acceso (user, biodata);
    // los de asistencia (att, el valor por omisión) las del PUSH de asistencia (USERINFO, FINGERTMP) y rechazan
    // las otras con -1004.
    private bool _isAccessProtocol;

    public void SetDevice(string serialNumber, string? deviceType = null)
    {
        _serialNumber = serialNumber;
        _isAccessProtocol = string.Equals(deviceType?.Trim(), "acc", StringComparison.OrdinalIgnoreCase);
    }

    public Task<bool> ConnectAsync(string ipAddress, int port, string? username = null, string? password = null, CancellationToken cancellationToken = default)
    {
        _ipAddress = ipAddress;
        _port = port;
        _username = username;
        _password = password;

        // En ADMS no hay conexión persistente desde el servidor al dispositivo.
        // Siempre retornamos true para permitir que el flujo de la aplicación continúe.
        _logger.LogInformation("Cliente ADMS 'conectado' virtualmente para {SN} ({Ip}:{Port})", _serialNumber ?? "desconocido", ipAddress, port);
        return Task.FromResult(true);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await CloseSdkSessionAsync();
        _sdkUnavailable = false;
    }

    /// <summary>La sesión SDK abierta, abriéndola la primera vez; null si el reloj no responde por SDK.</summary>
    private async Task<GrpcZKTecoDeviceClient?> GetSdkSessionAsync(CancellationToken cancellationToken)
    {
        if (_sdkSession != null) return _sdkSession;
        if (_sdkUnavailable || string.IsNullOrEmpty(_ipAddress) || _port <= 0) return null;

        var sdkClient = _serviceProvider.GetRequiredService<GrpcZKTecoDeviceClient>();
        if (await sdkClient.ConnectAsync(_ipAddress, _port, _username, _password, cancellationToken))
            return _sdkSession = sdkClient;

        _logger.LogWarning("El dispositivo ADMS {SN} no responde por SDK; los usuarios se enviarán por ADMS", _serialNumber);
        _sdkUnavailable = true;
        return null;
    }

    private async Task CloseSdkSessionAsync()
    {
        if (_sdkSession == null) return;
        try
        {
            await _sdkSession.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cerrar la sesión SDK del dispositivo ADMS {SN}", _serialNumber);
        }
        _sdkSession = null;
    }

    public Task<DeviceLogsResult> GetAttendanceLogsAsync(string deviceId, DateTime? fromDate, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        // Logs en ADMS son empujados en tiempo real vía HTTP push.
        return Task.FromResult(DeviceLogsResult.Empty());
    }

    public async Task<bool> ClearLogsAsync(
        string deviceId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return false;
        _logger.LogWarning("Comando de limpieza remota ADMS CLEAR ATTLOG encolado para SN {SN}. Atención: ADMS ejecuta borrado total del buffer del reloj.", _serialNumber);
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, "CLEAR ATTLOG", cancellationToken: cancellationToken);
        return true;
    }

    public async Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Intento 1: SDK (solo información del dispositivo)
        if (!string.IsNullOrEmpty(_ipAddress) && _port > 0)
        {
            try
            {
                _logger.LogInformation("Intentando obtener info del dispositivo vía SDK como primera opción para ADMS {SN} ({Ip}:{Port})", _serialNumber ?? "desconocido", _ipAddress, _port);
                var sdkClient = _serviceProvider.GetRequiredService<GrpcZKTecoDeviceClient>();
                var connected = await sdkClient.ConnectAsync(_ipAddress, _port, _username, _password, cancellationToken);
                if (connected)
                {
                    try
                    {
                        var result = await sdkClient.GetDeviceInfoAsync(cancellationToken);
                        if (result != null)
                        {
                            _logger.LogInformation("Info obtenida exitosamente vía SDK del dispositivo ADMS {SN}", _serialNumber);
                            return result;
                        }
                    }
                    finally
                    {
                        await sdkClient.DisconnectAsync(cancellationToken);
                    }
                }
                _logger.LogWarning("No se pudo conectar u obtener info del dispositivo {SN} vía SDK. Procediendo a fallback vía comando ADMS...", _serialNumber);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excepción al intentar usar SDK para obtener info del dispositivo {SN}. Procediendo a fallback vía comando ADMS...", _serialNumber);
            }
        }

        // Intento 2: Fallback vía comando ADMS (solo información del dispositivo)
        if (!string.IsNullOrEmpty(_serialNumber))
        {
            try
            {
                _logger.LogInformation("Intentando obtener info del dispositivo vía comando ADMS (fallback) para {SN}", _serialNumber);

                cancellationToken.ThrowIfCancellationRequested();
                // La respuesta llega a la fila del comando: no se pierde aunque la atienda otro proceso o llegue antes de esperar.
                // INFO consulta información técnica; no dispara consultas de usuarios, biometría o marcajes.
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                try
                {
                    var infoCommand = await _admsCommandService.EnqueueCommandAsync(_serialNumber, "INFO", cancellationToken: cancellationToken);
                    var admsInfo = await _admsCommandService.WaitForDeviceInfoAsync(infoCommand, TimeSpan.FromSeconds(30), waitCancellation.Token);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (admsInfo != null)
                    {
                        _logger.LogInformation("Info obtenida exitosamente vía comando ADMS del dispositivo {SN}", _serialNumber);
                        return admsInfo;
                    }

                    _logger.LogWarning("No hubo respuesta a través de comando ADMS para {SN} dentro del tiempo límite.", _serialNumber);
                }
                finally
                {
                    await waitCancellation.CancelAsync();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excepción al intentar obtener info vía comando ADMS para {SN}.", _serialNumber);
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return new List<DeviceUserDto>();

        // Intento 1: SDK
        if (!string.IsNullOrEmpty(_ipAddress) && _port > 0)
        {
            try
            {
                _logger.LogInformation("Intentando obtener todos los usuarios vía SDK como primera opción para ADMS {SN}", _serialNumber);
                var sdkClient = _serviceProvider.GetRequiredService<GrpcZKTecoDeviceClient>();
                var connected = await sdkClient.ConnectAsync(_ipAddress, _port, _username, _password, cancellationToken);
                if (connected)
                {
                    try
                    {
                        var result = await sdkClient.GetAllUsersAsync(cancellationToken);
                        if (result != null && result.Count > 0)
                        {
                            _logger.LogInformation("{Count} usuarios obtenidos exitosamente vía SDK del dispositivo ADMS {SN}", result.Count, _serialNumber);
                            return result;
                        }
                    }
                    finally
                    {
                        await sdkClient.DisconnectAsync(cancellationToken);
                    }
                }
                _logger.LogWarning("No se pudo obtener usuarios vía SDK, se procederá a encolar comandos ADMS");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excepción al intentar usar SDK para obtener usuarios. Fallback a ADMS.");
            }
        }

        // Encolar consulta de usuarios, fotos y biometría usando sintaxis estándar Push 3.x
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, "DATA QUERY tablename=user,fields=*", cancellationToken: cancellationToken);
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, "DATA QUERY tablename=userpic,fields=*", cancellationToken: cancellationToken);
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, "DATA QUERY tablename=biophoto,fields=*", cancellationToken: cancellationToken);
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, "DATA QUERY tablename=biodata,fields=*", cancellationToken: cancellationToken);

        _logger.LogInformation("Comandos de consulta de datos encolados para dispositivo ADMS {SN}", _serialNumber);

        // Retornamos lista vacía porque la respuesta llegará asíncronamente vía AdmsController
        return new List<DeviceUserDto>();
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return false;
        var command = _isAccessProtocol ? $"DATA DELETE user\tPIN={userId}" : $"DATA DELETE USERINFO PIN={userId}";
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, command, cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return false;
        var command = _isAccessProtocol ? $"DATA DELETE biodata\tPIN={userId}\tType=0" : $"DATA DELETE FINGERTMP PIN={userId}";
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, command, cancellationToken: cancellationToken);
        return true;
    }

    public Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public async Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return false;

        // Sin desfase conocido sólo se fija la hora: nunca se impone la zona del servidor.
        await _admsCommandService.EnqueueCommandAsync(_serialNumber, $"SET OPTIONS DateTime={dateTime:yyyy-MM-dd HH:mm:ss}", cancellationToken: cancellationToken);
        _logger.LogInformation("ADMS: Encolada hora {DateTime:yyyy-MM-dd HH:mm:ss} para SN: {SN}", dateTime, _serialNumber);
        return true;
    }

    public async Task<bool> SetDeviceClockAsync(DateTime civilTime, TimeSpan utcOffset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return false;

        // Zona del propio reloj (no la del servidor) y sin horario de verano: el reloj calcula su hora con el
        // encabezado Date (UTC) más este desfase, así que debe coincidir con la hora civil enviada.
        foreach (var clockCommand in AttendanceSystem.Application.Common.AdmsTimeZone.ClockCommands(civilTime, utcOffset))
        {
            await _admsCommandService.EnqueueCommandAsync(_serialNumber, clockCommand, cancellationToken: cancellationToken);
        }

        _logger.LogInformation("ADMS: Encolados comandos de zona horaria (UTC{Offset}) y hora ({DateTime:yyyy-MM-dd HH:mm:ss}) para SN: {SN}",
            utcOffset.ToString(@"\+hh\:mm;\-hh\:mm"), civilTime, _serialNumber);

        return true;
    }

    public async Task<DeviceUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Intento 1: SDK
        if (!string.IsNullOrEmpty(_ipAddress) && _port > 0)
        {
            try
            {
                _logger.LogInformation("Intentando obtener usuario {UserId} vía SDK como primera opción para ADMS {SN}", userId, _serialNumber);
                var sdkClient = _serviceProvider.GetRequiredService<GrpcZKTecoDeviceClient>();
                var connected = await sdkClient.ConnectAsync(_ipAddress, _port, _username, _password, cancellationToken);
                if (connected)
                {
                    try
                    {
                        var result = await sdkClient.GetUserAsync(userId, cancellationToken);
                        if (result != null)
                        {
                            _logger.LogInformation("Usuario {UserId} obtenido exitosamente vía SDK del dispositivo ADMS {SN}", userId, _serialNumber);
                            return result;
                        }
                    }
                    finally
                    {
                        await sdkClient.DisconnectAsync(cancellationToken);
                    }
                }
                _logger.LogWarning("No se pudo obtener usuario {UserId} vía SDK, pero ADMS no soporta GetUser de forma síncrona", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excepción al intentar usar SDK para obtener usuario.");
            }
        }

        // Not implemented for ADMS (Push protocol)
        return null;
    }

    public async Task<bool> SetUserAsync(DeviceUserDto user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return false;

        // Intento 1: SDK, sobre la sesión que comparten los envíos
        try
        {
            var sdkClient = await GetSdkSessionAsync(cancellationToken);
            if (sdkClient != null)
            {
                _logger.LogInformation("Intentando enviar usuario {UserId} vía SDK como primera opción para ADMS {SN}", user.UserId, _serialNumber);
                if (await sdkClient.SetUserAsync(user, cancellationToken))
                {
                    _logger.LogInformation("Usuario {UserId} enviado exitosamente vía SDK al dispositivo ADMS {SN}", user.UserId, _serialNumber);
                    return true;
                }
                _logger.LogWarning("No se pudo enviar usuario {UserId} vía SDK, se procederá por ADMS", user.UserId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // La sesión pudo quedar rota: los siguientes usuarios van directo por ADMS.
            _logger.LogWarning(ex, "Excepción al intentar usar SDK para enviar usuario. Fallback a ADMS.");
            await CloseSdkSessionAsync();
            _sdkUnavailable = true;
        }

        // Intento 2: ADMS (Push protocol)
        _logger.LogInformation("Enviando usuario {UserId} vía ADMS para {SN}", user.UserId, _serialNumber);

        // 1. Información de Usuario
        var admsPrivilege = DevicePrivilegeMapper.MapToProtocolValue(DeviceBrand.ZKTeco, DeviceDownloadMethod.Adms, (DevicePrivilege)user.Privilege);
        // Mismo limpiado que por SDK: sin acentos y con el largo que acepta el reloj, o lo guarda sin nombre.
        var name = AttendanceSystem.Application.Features.Devices.DeviceUserName.ForZkDevice(user.Name);
        var userCmd = $"PIN={user.UserId}\tName={name}\tPri={admsPrivilege}";
        if (!string.IsNullOrEmpty(user.Password)) userCmd += $"\tPasswd={user.Password}";
        if (!string.IsNullOrEmpty(user.CardNumber)) userCmd += $"\tCard={user.CardNumber}";

        await _admsCommandService.EnqueueCommandAsync(_serialNumber,
            _isAccessProtocol ? $"DATA UPDATE user\t{userCmd}" : $"DATA UPDATE USERINFO {userCmd}", cancellationToken: cancellationToken);

        // 2. Huellas Digitales
        if (user.Fingerprints != null && user.Fingerprints.Any())
        {
            foreach (var fp in user.Fingerprints)
            {
                await _admsCommandService.EnqueueCommandAsync(_serialNumber,
                    _isAccessProtocol
                        ? $"DATA UPDATE biodata\tPIN={user.UserId}\tType=0\tIndex={fp.Index}\tVersion=10.0\tContent={fp.Template}"
                        : $"DATA UPDATE FINGERTMP PIN={user.UserId}\tFID={fp.Index}\tSize={fp.Template.Length}\tValid=1\tTMP={fp.Template}",
                    cancellationToken: cancellationToken);
            }
        }

        // 3. Rostro (Face)
        if (!string.IsNullOrEmpty(user.FaceTemplate))
        {
            // Type 9 = Face (luz visible)
            await _admsCommandService.EnqueueCommandAsync(_serialNumber,
                _isAccessProtocol
                    ? $"DATA UPDATE biodata\tPIN={user.UserId}\tType=9\tVersion=58.0\tContent={user.FaceTemplate}"
                    : $"DATA UPDATE BIODATA Pin={user.UserId}\tNo=0\tIndex=0\tValid=1\tDuress=0\tType=9\tMajorVer=58\tMinorVer=0\tFormat=0\tTmp={user.FaceTemplate}",
                cancellationToken: cancellationToken);
        }

        // 4. Fotografía de Perfil
        if (!string.IsNullOrEmpty(user.Photo))
        {
            await _admsCommandService.EnqueueCommandAsync(_serialNumber,
                _isAccessProtocol
                    ? $"DATA UPDATE userpic\tPIN={user.UserId}\tContent={user.Photo}"
                    : $"DATA UPDATE USERPIC PIN={user.UserId}\tSize={user.Photo.Length}\tContent={user.Photo}",
                cancellationToken: cancellationToken);
        }

        return true;
    }
}
