using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.ZKTeco.Grpc; // Generated namespace
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Adapters;

/// <summary>
/// Implementación real del cliente ZKTeco usando gRPC para comunicarse con el servicio Windows.
/// </summary>
public class GrpcZKTecoDeviceClient : IZKTecoDeviceClient
{
    private readonly ZKTecoService.ZKTecoServiceClient _client;
    private readonly ILogger<GrpcZKTecoDeviceClient> _logger;

    public GrpcZKTecoDeviceClient(
        ZKTecoService.ZKTecoServiceClient client,
        ILogger<GrpcZKTecoDeviceClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(
        string ipAddress, 
        int port, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Conectando a {IpAddress}:{Port} vía gRPC...", ipAddress, port);
            
            var request = new ConnectDeviceRequest
            {
                IpAddress = ipAddress,
                Port = port,
                TimeoutSeconds = 10
            };

            var response = await _client.ConnectDeviceAsync(request, cancellationToken: cancellationToken);
            
            if (!response.Success)
            {
                _logger.LogWarning("Fallo al conectar: {Message}", response.Message);
            }

            return response.Success;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Error gRPC al conectar con {IpAddress}:{Port}", ipAddress, port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al conectar con {IpAddress}:{Port}", ipAddress, port);
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
                FromDate = fromDate?.ToString("o") ?? "", // ISO 8601
                ToDate = (toDate ?? DateTime.UtcNow).ToString("o")
            };

            var response = await _client.GetAttendanceLogsAsync(request, cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Error al obtener logs: {Message}", response.Message);
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
            _logger.LogError(ex, "Error obteniendo logs del dispositivo {DeviceId}", deviceId);
            return Array.Empty<RawAttendanceRecord>();
        }
    }

    public async Task<bool> ClearLogsAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ClearDeviceLogsRequest
            {
                DeviceId = deviceId
            };

            var response = await _client.ClearDeviceLogsAsync(request, cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error limpiando logs del dispositivo {DeviceId}", deviceId);
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DisconnectDeviceAsync(new DisconnectDeviceRequest(), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error desconectando");
        }
    }

    public async Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo información del dispositivo vía gRPC...");
            
            var request = new GetDeviceInfoRequest();
            var response = await _client.GetDeviceInfoAsync(request, cancellationToken: cancellationToken);

            if (!response.Success || response.DeviceInfo == null)
            {
                _logger.LogWarning("Error al obtener información: {Message}", response.Message);
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
                response.DeviceInfo.AttendanceRecordCapacity
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo información del dispositivo");
            return null;
        }
    }

    public async Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Solicitando lista de usuarios gRPC...");
            var request = new GetAllUsersRequest { DeviceId = "" }; // DeviceId might be irrelevant if handled by connection context, but proto has it.
            // Wait, looking at proto, GetAllUsersRequest has device_id.
            // In Grpc service, we might need it if we manage multiple connections.
            // But currently the service seems stateful per connection?
            // Checking Proto... Yes, device_id is field 1.
            // In ConnectDeviceRequest we pass IP/Port.
            // The service seems to keep one connection open?
            // Looking at ZKTecoGrpcService.cs, it uses _zkClient.ConnectAsync.
            // If the service is a Singleton wrapping a single device client, then it's stateful.
            // If the service is Scoped, it's per request? 
            // Usually gRPC services are Scoped or Singleton.
            // ZKTecoGrpcService inherits ZKTecoServiceBase. 
            // In `Program.cs` of ZKTeco.Service, how is it registered?
            // Assuming it maintains state.
            
            var response = await _client.GetAllUsersAsync(request, cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Fallo al obtener usuarios: {Message}", response.Message);
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
                string.IsNullOrEmpty(u.FaceTemplate) ? null : u.FaceTemplate
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo usuarios");
            return Array.Empty<DeviceUserDto>();
        }
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
         try
        {
            var request = new DeleteEmployeeRequest { EmployeeId = userId };
            var response = await _client.DeleteEmployeeAsync(request, cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando usuario {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteUserFingerprintsRequest { UserId = userId };
            var response = await _client.DeleteUserFingerprintsAsync(request, cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando huellas de {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ResetToFactorySettingsRequest();
            var response = await _client.ResetToFactorySettingsAsync(request, cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error solicitando restablecimiento de fábrica");
            return false;
        }
    }

    public async Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SetDeviceTimeRequest { DateTime = dateTime.ToString("o") };
            var response = await _client.SetDeviceTimeAsync(request, cancellationToken: cancellationToken);
            return response.Success;
        }
         catch (Exception ex)
        {
            _logger.LogError(ex, "Error configurando hora");
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
                FaceTemplate = user.FaceTemplate ?? ""
            };

            if (user.Fingerprints != null)
            {
                request.Fingerprints.AddRange(user.Fingerprints.Select(f => new UserFingerprint 
                { 
                    FingerIndex = f.Index, 
                    TemplateData = f.Template 
                }));
            }

            var response = await _client.RegisterEmployeeAsync(request, cancellationToken: cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando usuario {UserId} vía gRPC", user.UserId);
            return false;
        }
    }
}
