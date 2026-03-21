using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AttendanceSystem.Infrastructure.Adapters;

public class AdmsDeviceClient : IDeviceClient
{
    private readonly IAdmsCommandService _admsCommandService;
    private readonly ILogger<AdmsDeviceClient> _logger;
    private string? _serialNumber;

    public AdmsDeviceClient(IAdmsCommandService admsCommandService, ILogger<AdmsDeviceClient> logger)
    {
        _admsCommandService = admsCommandService;
        _logger = logger;
    }

    public void SetDevice(string serialNumber)
    {
        _serialNumber = serialNumber;
    }

    public Task<bool> ConnectAsync(string ipAddress, int port, string? username = null, string? password = null, CancellationToken cancellationToken = default)
    {
        // En ADMS no hay conexión persistente desde el servidor al dispositivo.
        // Siempre retornamos true para permitir que el flujo de la aplicación continúe.
        _logger.LogInformation("Cliente ADMS 'conectado' virtualmente para {SN}", _serialNumber ?? "desconocido");
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<RawAttendanceRecord>> GetAttendanceLogsAsync(string deviceId, DateTime? fromDate, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        // Logs en ADMS son empujados en tiempo real.
        return Task.FromResult<IReadOnlyList<RawAttendanceRecord>>(new List<RawAttendanceRecord>());
    }

    public Task<bool> ClearLogsAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, "CLEAR ATTLOG");
        return Task.FromResult(true);
    }

    public Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult<DeviceInfoDto?>(null);

    public Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult<IReadOnlyList<DeviceUserDto>>(new List<DeviceUserDto>());

        // Para equipos modernos (especialmente Visible Light), la sintaxis suele ser 'DATA QUERY TABLE=NombreTabla'
        // Intentamos esta versión para resolver el error -629.
        _admsCommandService.EnqueueCommand(_serialNumber, "DATA QUERY TABLE=USERINFO");
        _admsCommandService.EnqueueCommand(_serialNumber, "DATA QUERY TABLE=USERPIC");
        _admsCommandService.EnqueueCommand(_serialNumber, "DATA QUERY TABLE=BIODATA");
        
        _logger.LogInformation("Comandos de consulta de datos encolados para dispositivo ADMS {SN}", _serialNumber);
        
        // Retornamos lista vacía porque la respuesta llegará asíncronamente vía AdmsController
        return Task.FromResult<IReadOnlyList<DeviceUserDto>>(new List<DeviceUserDto>());
    }

    public Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, $"DATA DELETE USERINFO PIN={userId}");
        return Task.FromResult(true);
    }

    public Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, $"DATA DELETE BIODATA PIN={userId}\tType=0");
        return Task.FromResult(true);
    }

    public Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);
        _admsCommandService.EnqueueCommand(_serialNumber, $"SET OPTIONS DateTime={dateTime:yyyy-MM-dd HH:mm:ss}");
        return Task.FromResult(true);
    }

    public Task<bool> SetUserAsync(DeviceUserDto user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_serialNumber)) return Task.FromResult(false);

        // 1. Información de Usuario
        var userCmd = $"PIN={user.UserId}\tName={user.Name}\tPrivilege=0"; // 0 = Usuario normal
        if (!string.IsNullOrEmpty(user.Password)) userCmd += $"\tPassword={user.Password}";
        if (!string.IsNullOrEmpty(user.CardNumber)) userCmd += $"\tCard={user.CardNumber}";
        
        _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE USERINFO {userCmd}");

        // 2. Huellas Digitales
        if (user.Fingerprints != null && user.Fingerprints.Any())
        {
            foreach (var fp in user.Fingerprints)
            {
                // Type 0 = Fingerprint
                _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE BIODATA PIN={user.UserId}\tType=0\tIndex={fp.Index}\tVersion=10.0\tContent={fp.Template}");
            }
        }

        // 3. Rostro (Face)
        if (!string.IsNullOrEmpty(user.FaceTemplate))
        {
            // Type 9 = Face
            _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE BIODATA PIN={user.UserId}\tType=9\tVersion=58.0\tContent={user.FaceTemplate}");
        }

        // 4. Fotografía de Perfil
        if (!string.IsNullOrEmpty(user.Photo))
        {
            _admsCommandService.EnqueueCommand(_serialNumber, $"DATA UPDATE USERPIC PIN={user.UserId}\tContent={user.Photo}");
        }

        return Task.FromResult(true);
    }
}
