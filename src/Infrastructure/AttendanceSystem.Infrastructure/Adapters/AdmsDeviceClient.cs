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

    public AdmsDeviceClient(IAdmsCommandService admsCommandService, IServiceProvider serviceProvider, ILogger<AdmsDeviceClient> logger)
    {
        _admsCommandService = admsCommandService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void SetDevice(string serialNumber)
    {
        _serialNumber = serialNumber;
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

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<RawAttendanceRecord>> GetAttendanceLogsAsync(string deviceId, DateTime? fromDate, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        // Logs en ADMS son empujados en tiempo real.
        return Task.FromResult<IReadOnlyList<RawAttendanceRecord>>(new List<RawAttendanceRecord>());
    }

    public Task<bool> ClearLogsAsync(
        string deviceId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, "CLEAR ATTLOG");
        return Task.FromResult(true);
    }

    public async Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        // Intento 1: SDK
        if (!string.IsNullOrEmpty(_ipAddress) && _port > 0)
        {
            try
            {
                _logger.LogInformation("Intentando obtener info del dispositivo vía SDK como primera opción para ADMS {SN}", _serialNumber);
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
                _logger.LogWarning("No se pudo obtener info del dispositivo {SN} vía SDK.", _serialNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excepción al intentar usar SDK para obtener info del dispositivo.");
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

        // Para equipos modernos (especialmente Visible Light), la sintaxis suele ser 'DATA QUERY TABLE=NombreTabla'
        // Intentamos esta versión para resolver el error -629.
        _admsCommandService.EnqueueCommand(_serialNumber, "DATA QUERY\tTABLE=USERINFO");
        _admsCommandService.EnqueueCommand(_serialNumber, "DATA QUERY\tTABLE=USERPIC");
        _admsCommandService.EnqueueCommand(_serialNumber, "DATA QUERY\tTABLE=BIODATA");
        
        _logger.LogInformation("Comandos de consulta de datos encolados para dispositivo ADMS {SN}", _serialNumber);
        
        // Retornamos lista vacía porque la respuesta llegará asíncronamente vía AdmsController
        return new List<DeviceUserDto>();
    }

    public Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, $"DATA DELETE USERINFO\tPIN={userId}");
        return Task.FromResult(true);
    }

    public Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, $"DATA DELETE BIODATA\tPIN={userId}\tType=0");
        return Task.FromResult(true);
    }

    public Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, $"SET OPTIONS DateTime={dateTime:yyyy-MM-dd HH:mm:ss}");
        return Task.FromResult(true);
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

        // Intento 1: SDK
        if (!string.IsNullOrEmpty(_ipAddress) && _port > 0)
        {
            try
            {
                _logger.LogInformation("Intentando enviar usuario {UserId} vía SDK como primera opción para ADMS {SN}", user.UserId, _serialNumber);
                var sdkClient = _serviceProvider.GetRequiredService<GrpcZKTecoDeviceClient>();
                var connected = await sdkClient.ConnectAsync(_ipAddress, _port, _username, _password, cancellationToken);
                if (connected)
                {
                    try
                    {
                        var result = await sdkClient.SetUserAsync(user, cancellationToken);
                        if (result)
                        {
                            _logger.LogInformation("Usuario {UserId} enviado exitosamente vía SDK al dispositivo ADMS {SN}", user.UserId, _serialNumber);
                            return true;
                        }
                    }
                    finally
                    {
                        await sdkClient.DisconnectAsync(cancellationToken);
                    }
                }
                _logger.LogWarning("No se pudo enviar usuario {UserId} vía SDK, se procederá por ADMS", user.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Excepción al intentar usar SDK para enviar usuario. Fallback a ADMS.");
            }
        }

        // Intento 2: ADMS (Push protocol)
        _logger.LogInformation("Enviando usuario {UserId} vía ADMS para {SN}", user.UserId, _serialNumber);

        // 1. Información de Usuario
        var userCmd = $"PIN={user.UserId}\tName={user.Name}\tPrivilege=0"; // 0 = Usuario normal
        if (!string.IsNullOrEmpty(user.Password)) userCmd += $"\tPassword={user.Password}";
        if (!string.IsNullOrEmpty(user.CardNumber)) userCmd += $"\tCard={user.CardNumber}";
        
        _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE USERINFO\t{userCmd}");

        // 2. Huellas Digitales
        if (user.Fingerprints != null && user.Fingerprints.Any())
        {
            foreach (var fp in user.Fingerprints)
            {
                _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE BIODATA\tPIN={user.UserId}\tType=0\tIndex={fp.Index}\tVersion=10.0\tContent={fp.Template}");
            }
        }

        // 3. Rostro (Face)
        if (!string.IsNullOrEmpty(user.FaceTemplate))
        {
            // Type 9 = Face
            _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE BIODATA\tPIN={user.UserId}\tType=9\tVersion=58.0\tContent={user.FaceTemplate}");
        }

        // 4. Fotografía de Perfil
        if (!string.IsNullOrEmpty(user.Photo))
        {
            _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE USERPIC\tPIN={user.UserId}\tContent={user.Photo}");
        }

        return true;
    }
}
