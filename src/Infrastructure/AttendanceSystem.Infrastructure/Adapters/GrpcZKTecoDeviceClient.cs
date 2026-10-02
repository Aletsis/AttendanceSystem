using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.ZKTeco.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Adapters;

/// <summary>
/// Implementación real del cliente ZKTeco usando gRPC para comunicarse con el servicio Windows.
/// Utiliza un identificador de sesión por instancia para garantizar concurrencia multidispositivo.
/// </summary>
public class GrpcZKTecoDeviceClient : IDeviceClient
{
    private readonly ZKTecoService.ZKTecoServiceClient _client;
    private readonly ILogger<GrpcZKTecoDeviceClient> _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");

    public GrpcZKTecoDeviceClient(
        ZKTecoService.ZKTecoServiceClient client,
        ILogger<GrpcZKTecoDeviceClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    private Metadata CreateHeaders() => new()
    {
        { "x-session-id", _sessionId }
    };

    public async Task<bool> ConnectAsync(string ipAddress, int port, string? username = null, string? password = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sesión {SessionId}: Conectando a {IpAddress}:{Port} vía gRPC...", _sessionId, ipAddress, port);

            var request = new ConnectDeviceRequest
            {
                IpAddress = ipAddress,
                Port = port,
                TimeoutSeconds = 30
            };

            var response = await _client.ConnectDeviceAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Sesión {SessionId}: Fallo al conectar: {Message}", _sessionId, response.Message);
            }

            return response.Success;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error gRPC al conectar con {IpAddress}:{Port}", _sessionId, ipAddress, port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error inesperado al conectar con {IpAddress}:{Port}", _sessionId, ipAddress, port);
            return false;
        }
    }

    public async Task<IReadOnlyList<RawAttendanceRecord>> GetAttendanceLogsAsync(
        string deviceId,
        DateTime? fromDate,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetAttendanceLogsRequest
            {
                DeviceId = deviceId,
                FromDate = fromDate?.ToString("o") ?? "",
                ToDate = (toDate ?? DateTime.UtcNow).ToString("o")
            };

            var response = await _client.GetAttendanceLogsAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Sesión {SessionId}: Error al obtener logs: {Message}", _sessionId, response.Message);
                return Array.Empty<RawAttendanceRecord>();
            }

            return response.Records.Select(r => new RawAttendanceRecord(
                r.UserId,
                DateTime.Parse(r.CheckTime),
                r.VerifyMode,
                r.InOutMode,
                r.WorkCode
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error obteniendo logs del dispositivo {DeviceId}", _sessionId, deviceId);
            return Array.Empty<RawAttendanceRecord>();
        }
    }

    public async Task<bool> ClearLogsAsync(
        string deviceId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ClearDeviceLogsRequest
            {
                DeviceId = deviceId,
                FromDate = fromDate?.ToString("o") ?? "",
                ToDate = toDate?.ToString("o") ?? ""
            };

            var response = await _client.ClearDeviceLogsAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error limpiando logs del dispositivo {DeviceId}", _sessionId, deviceId);
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DisconnectDeviceAsync(new DisconnectDeviceRequest(), headers: CreateHeaders(), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error desconectando", _sessionId);
        }
    }

    public async Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sesión {SessionId}: Obteniendo información del dispositivo vía gRPC...", _sessionId);

            var request = new GetDeviceInfoRequest();
            var response = await _client.GetDeviceInfoAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);

            if (!response.Success || response.DeviceInfo == null)
            {
                _logger.LogWarning("Sesión {SessionId}: Error al obtener información: {Message}", _sessionId, response.Message);
                return null;
            }

            return new DeviceInfoDto(
                response.DeviceInfo.SerialNumber,
                response.DeviceInfo.DeviceName,
                response.DeviceInfo.FirmwareVersion,
                response.DeviceInfo.Platform,
                response.DeviceInfo.UserCount,
                response.DeviceInfo.FingerprintCount,
                response.DeviceInfo.FaceCount,
                response.DeviceInfo.AttendanceRecordCount,
                response.DeviceInfo.UserCapacity,
                response.DeviceInfo.FingerprintCapacity,
                response.DeviceInfo.FaceCapacity,
                response.DeviceInfo.AttendanceRecordCapacity,
                string.IsNullOrWhiteSpace(response.DeviceInfo.PushVersion) ? null : response.DeviceInfo.PushVersion,
                string.IsNullOrWhiteSpace(response.DeviceInfo.SdkVersion) ? null : response.DeviceInfo.SdkVersion,
                string.IsNullOrWhiteSpace(response.DeviceInfo.MacAddress) ? null : response.DeviceInfo.MacAddress
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error obteniendo información del dispositivo", _sessionId);
            return null;
        }
    }

    public async Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sesión {SessionId}: Solicitando lista de usuarios gRPC...", _sessionId);
            var request = new GetAllUsersRequest { DeviceId = "" };

            var response = await _client.GetAllUsersAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Sesión {SessionId}: Fallo al obtener usuarios: {Message}", _sessionId, response.Message);
                return Array.Empty<DeviceUserDto>();
            }

            return response.Users.Select(u => new DeviceUserDto(
                u.UserId,
                u.Name,
                u.Password,
                u.Privilege,
                u.Enabled,
                string.IsNullOrEmpty(u.CardNumber) ? null : u.CardNumber,
                u.Fingerprints.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.TemplateData)).ToList(),
                string.IsNullOrEmpty(u.FaceTemplate) ? null : u.FaceTemplate,
                string.IsNullOrEmpty(u.Photo) ? null : u.Photo
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error obteniendo usuarios", _sessionId);
            return Array.Empty<DeviceUserDto>();
        }
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteEmployeeRequest { EmployeeId = userId };
            var response = await _client.DeleteEmployeeAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error eliminando usuario {UserId}", _sessionId, userId);
            return false;
        }
    }

    public async Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteUserFingerprintsRequest { UserId = userId };
            var response = await _client.DeleteUserFingerprintsAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error eliminando huellas de {UserId}", _sessionId, userId);
            return false;
        }
    }

    public async Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ResetToFactorySettingsRequest();
            var response = await _client.ResetToFactorySettingsAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error solicitando restablecimiento de fábrica", _sessionId);
            return false;
        }
    }

    public async Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SetDeviceTimeRequest { DateTime = dateTime.ToString("o") };
            var response = await _client.SetDeviceTimeAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error configurando hora", _sessionId);
            return false;
        }
    }

    public async Task<bool> SetUserAsync(DeviceUserDto user, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new RegisterEmployeeRequest
            {
                DeviceId = "",
                EmployeeId = user.UserId,
                Name = user.Name,
                Password = user.Password,
                Privilege = user.Privilege,
                Enabled = user.Enabled,
                CardNumber = user.CardNumber ?? "",
                FaceTemplate = user.FaceTemplate ?? "",
                Photo = user.Photo ?? ""
            };

            if (user.Fingerprints != null)
            {
                request.Fingerprints.AddRange(user.Fingerprints.Select(f => new UserFingerprint
                {
                    FingerIndex = f.Index,
                    TemplateData = f.Template
                }));
            }

            var response = await _client.RegisterEmployeeAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error enviando usuario {UserId} vía gRPC", _sessionId, user.UserId);
            return false;
        }
    }

    public async Task<DeviceUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetEmployeeRequest { EmployeeId = userId };
            var response = await _client.GetEmployeeAsync(request, headers: CreateHeaders(), cancellationToken: cancellationToken);

            if (!response.Success || response.Employee == null)
            {
                return null;
            }

            var e = response.Employee;
            return new DeviceUserDto(
                e.UserId,
                e.Name,
                e.Password,
                e.Privilege,
                e.Enabled,
                string.IsNullOrEmpty(e.CardNumber) ? null : e.CardNumber,
                e.Fingerprints.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.TemplateData)).ToList(),
                string.IsNullOrEmpty(e.FaceTemplate) ? null : e.FaceTemplate,
                string.IsNullOrEmpty(e.Photo) ? null : e.Photo
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error obteniendo usuario {UserId} vía gRPC", _sessionId, userId);
            return null;
        }
    }
}
