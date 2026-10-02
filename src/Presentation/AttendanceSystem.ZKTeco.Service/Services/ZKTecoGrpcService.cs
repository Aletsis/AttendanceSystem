using Grpc.Core;
using AttendanceSystem.ZKTeco.Grpc;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.ZKTeco.Adapters;
using System.Globalization;

namespace AttendanceSystem.ZKTeco.Service.Services;

public class ZKTecoGrpcService : AttendanceSystem.ZKTeco.Grpc.ZKTecoService.ZKTecoServiceBase
{
    private readonly ILogger<ZKTecoGrpcService> _logger;
    private readonly IZKTecoSessionManager _sessionManager;
    private readonly IDeviceDiscoveryService _discoveryService;

    public ZKTecoGrpcService(
        ILogger<ZKTecoGrpcService> logger,
        IZKTecoSessionManager sessionManager,
        IDeviceDiscoveryService discoveryService)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _discoveryService = discoveryService;
    }

    private string GetSessionId(ServerCallContext context)
    {
        var sessionHeader = context.RequestHeaders.Get("x-session-id");
        return !string.IsNullOrWhiteSpace(sessionHeader?.Value) ? sessionHeader.Value : "default-session";
    }

    public override async Task<ConnectDeviceResponse> ConnectDevice(ConnectDeviceRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        var timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 10;
        _logger.LogInformation("Sesión {SessionId}: Conectando a {IpAddress}:{Port} (Timeout: {Timeout}s)...",
            sessionId, request.IpAddress, request.Port, timeout);

        try
        {
            var client = await _sessionManager.ConnectSessionAsync(
                sessionId,
                request.IpAddress,
                request.Port,
                timeout,
                context.CancellationToken);

            var info = await client.GetDeviceInfoAsync(context.CancellationToken);

            return new ConnectDeviceResponse
            {
                Success = true,
                Message = "Conexión exitosa",
                DeviceSerialNumber = info?.SerialNumber ?? "UNKNOWN"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Excepción al conectar con dispositivo {Ip}:{Port}",
                sessionId, request.IpAddress, request.Port);
            return new ConnectDeviceResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<GetAttendanceLogsResponse> GetAttendanceLogs(GetAttendanceLogsRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Obteniendo logs de {DeviceId}...", sessionId, request.DeviceId);

        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);

            DateTime? fromDate = null;
            if (!string.IsNullOrEmpty(request.FromDate) && DateTime.TryParse(request.FromDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fDate))
            {
                fromDate = fDate;
            }

            DateTime? toDate = null;
            if (!string.IsNullOrEmpty(request.ToDate) && DateTime.TryParse(request.ToDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var tDate))
            {
                toDate = tDate;
            }

            var logs = await client.GetAttendanceLogsAsync(request.DeviceId, fromDate, toDate, context.CancellationToken);

            var response = new GetAttendanceLogsResponse
            {
                Success = true,
                Message = $"Se obtuvieron {logs.Count} registros",
                TotalCount = logs.Count
            };

            foreach (var log in logs)
            {
                response.Records.Add(new AttendanceRecord
                {
                    DeviceId = request.DeviceId,
                    UserId = log.UserId,
                    CheckTime = log.CheckTime.ToString("o"),
                    VerifyMode = log.VerifyMethod,
                    InOutMode = log.InOutMode,
                    WorkCode = log.WorkCode
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error obteniendo logs", sessionId);
            return new GetAttendanceLogsResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<ClearDeviceLogsResponse> ClearDeviceLogs(ClearDeviceLogsRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Limpiando logs de {DeviceId}...", sessionId, request.DeviceId);

        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);

            DateTime? fromDate = null;
            if (!string.IsNullOrEmpty(request.FromDate) && DateTime.TryParse(request.FromDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fDate))
            {
                fromDate = fDate;
            }

            DateTime? toDate = null;
            if (!string.IsNullOrEmpty(request.ToDate) && DateTime.TryParse(request.ToDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var tDate))
            {
                toDate = tDate;
            }

            var success = await client.ClearLogsAsync(request.DeviceId, fromDate, toDate, context.CancellationToken);
            return new ClearDeviceLogsResponse
            {
                Success = success,
                Message = success ? "Logs limpiados correctamente" : "Fallo al limpiar logs"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error limpiando logs", sessionId);
            return new ClearDeviceLogsResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<DisconnectDeviceResponse> DisconnectDevice(DisconnectDeviceRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Desconectando dispositivo...", sessionId);
        try
        {
            await _sessionManager.DisconnectSessionAsync(sessionId, context.CancellationToken);
            return new DisconnectDeviceResponse { Success = true, Message = "Desconectado" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sesión {SessionId}: Error desconectando sesión", sessionId);
            return new DisconnectDeviceResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<GetDeviceInfoResponse> GetDeviceInfo(GetDeviceInfoRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Obteniendo información del dispositivo...", sessionId);

        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var deviceInfo = await client.GetDeviceInfoAsync(context.CancellationToken);

            if (deviceInfo == null)
            {
                return new GetDeviceInfoResponse
                {
                    Success = false,
                    Message = "No se pudo obtener la información del dispositivo"
                };
            }

            return new GetDeviceInfoResponse
            {
                Success = true,
                Message = "Información obtenida exitosamente",
                DeviceInfo = new AttendanceSystem.ZKTeco.Grpc.DeviceInfo
                {
                    SerialNumber = deviceInfo.SerialNumber,
                    DeviceName = deviceInfo.DeviceName,
                    FirmwareVersion = deviceInfo.FirmwareVersion,
                    Platform = deviceInfo.Platform,
                    UserCount = deviceInfo.UserCount,
                    FingerprintCount = deviceInfo.FingerprintCount,
                    FaceCount = deviceInfo.FaceCount,
                    AttendanceRecordCount = deviceInfo.AttendanceRecordCount,
                    UserCapacity = deviceInfo.UserCapacity,
                    FingerprintCapacity = deviceInfo.FingerprintCapacity,
                    FaceCapacity = deviceInfo.FaceCapacity,
                    AttendanceRecordCapacity = deviceInfo.AttendanceRecordCapacity,
                    PushVersion = deviceInfo.PushVersion ?? string.Empty,
                    SdkVersion = deviceInfo.SdkVersion ?? string.Empty,
                    MacAddress = deviceInfo.MacAddress ?? string.Empty
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error al obtener información del dispositivo", sessionId);
            return new GetDeviceInfoResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<DeleteEmployeeResponse> DeleteEmployee(DeleteEmployeeRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Eliminando empleado {EmployeeId} de {DeviceId}...",
            sessionId, request.EmployeeId, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var success = await client.DeleteUserAsync(request.EmployeeId, context.CancellationToken);
            return new DeleteEmployeeResponse
            {
                Success = success,
                Message = success ? "Empleado eliminado" : "No se pudo eliminar el empleado"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error eliminando empleado", sessionId);
            return new DeleteEmployeeResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<GetEmployeeResponse> GetEmployee(GetEmployeeRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Obteniendo empleado {EmployeeId} de {DeviceId}...",
            sessionId, request.EmployeeId, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var user = await client.GetUserAsync(request.EmployeeId, context.CancellationToken);

            if (user == null)
            {
                return new GetEmployeeResponse { Success = false, Message = "Empleado no encontrado" };
            }

            var protoUser = new AttendanceSystem.ZKTeco.Grpc.DeviceUser
            {
                UserId = user.UserId,
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
                protoUser.Fingerprints.AddRange(user.Fingerprints.Select(f => new UserFingerprint
                {
                    FingerIndex = f.Index,
                    TemplateData = f.Template
                }));
            }

            return new GetEmployeeResponse
            {
                Success = true,
                Message = "Empleado obtenido exitosamente",
                Employee = protoUser
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error al obtener empleado", sessionId);
            return new GetEmployeeResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<GetAllUsersResponse> GetAllUsers(GetAllUsersRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Obteniendo usuarios de {DeviceId}...", sessionId, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var users = await client.GetAllUsersAsync(context.CancellationToken);
            var response = new GetAllUsersResponse
            {
                Success = true,
                Message = $"Se obtuvieron {users.Count} usuarios"
            };

            foreach (var u in users)
            {
                var protoUser = new AttendanceSystem.ZKTeco.Grpc.DeviceUser
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Password = u.Password,
                    Privilege = u.Privilege,
                    Enabled = u.Enabled,
                    CardNumber = u.CardNumber ?? "",
                    FaceTemplate = u.FaceTemplate ?? "",
                    Photo = u.Photo ?? ""
                };
                if (u.Fingerprints != null)
                {
                    protoUser.Fingerprints.AddRange(u.Fingerprints.Select(f => new UserFingerprint
                    {
                        FingerIndex = f.Index,
                        TemplateData = f.Template
                    }));
                }
                response.Users.Add(protoUser);
            }
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error obteniendo usuarios", sessionId);
            return new GetAllUsersResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<DeleteUserFingerprintsResponse> DeleteUserFingerprints(DeleteUserFingerprintsRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Eliminando huellas de {UserId} en {DeviceId}...",
            sessionId, request.UserId, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var success = await client.DeleteUserFingerprintsAsync(request.UserId, context.CancellationToken);
            return new DeleteUserFingerprintsResponse
            {
                Success = success,
                Message = success ? "Huellas eliminadas" : "No se pudieron eliminar las huellas"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error eliminando huellas", sessionId);
            return new DeleteUserFingerprintsResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<ResetToFactorySettingsResponse> ResetToFactorySettings(ResetToFactorySettingsRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Restableciendo valores de fábrica en {DeviceId}...", sessionId, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var success = await client.ResetToFactorySettingsAsync(context.CancellationToken);
            return new ResetToFactorySettingsResponse
            {
                Success = success,
                Message = success ? "Restablecimiento exitoso (Datos borrados)" : "Fallo el restablecimiento"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error restableciendo valores de fábrica", sessionId);
            return new ResetToFactorySettingsResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<SetDeviceTimeResponse> SetDeviceTime(SetDeviceTimeRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Configurando hora en {DeviceId}...", sessionId, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            if (!DateTime.TryParse(request.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            {
                return new SetDeviceTimeResponse { Success = false, Message = "Formato de fecha inválido" };
            }

            var success = await client.SetDeviceTimeAsync(dt, context.CancellationToken);
            return new SetDeviceTimeResponse
            {
                Success = success,
                Message = success ? "Hora configurada exitosamente" : "Fallo al configurar hora"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error configurando hora", sessionId);
            return new SetDeviceTimeResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<RegisterEmployeeResponse> RegisterEmployee(RegisterEmployeeRequest request, ServerCallContext context)
    {
        var sessionId = GetSessionId(context);
        _logger.LogInformation("Sesión {SessionId}: Registrando usuario {UserId} ({Name}) en {DeviceId}...",
            sessionId, request.EmployeeId, request.Name, request.DeviceId);
        try
        {
            var client = await _sessionManager.GetSessionAsync(sessionId);
            var userDto = new Application.DTOs.DeviceUserDto(
               request.EmployeeId,
               request.Name,
               request.Password,
               request.Privilege,
               request.Enabled,
               string.IsNullOrEmpty(request.CardNumber) ? null : request.CardNumber,
               request.Fingerprints.Select(f => new Application.DTOs.DeviceFingerprintDto(f.FingerIndex, f.TemplateData)).ToList(),
               string.IsNullOrEmpty(request.FaceTemplate) ? null : request.FaceTemplate,
               Photo: string.IsNullOrEmpty(request.Photo) ? null : request.Photo);

            var success = await client.SetUserAsync(userDto, context.CancellationToken);

            return new RegisterEmployeeResponse
            {
                Success = success,
                Message = success ? "Usuario registrado/actualizado correctamente" : "Fallo al registrar usuario"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesión {SessionId}: Error registrando usuario {UserId}", sessionId, request.EmployeeId);
            return new RegisterEmployeeResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<DiscoverDevicesResponse> DiscoverDevices(DiscoverDevicesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Recibida petición de descubrimiento de dispositivos...");
        try
        {
            var devices = await _discoveryService.DiscoverDevicesAsync(context.CancellationToken);

            var response = new DiscoverDevicesResponse
            {
                Success = true,
                Message = $"Se encontraron {devices.Count} dispositivos"
            };

            foreach (var d in devices)
            {
                response.Devices.Add(new DiscoveredDevice
                {
                    IpAddress = d.IpAddress,
                    SerialNumber = d.SerialNumber,
                    DeviceName = d.DeviceName,
                    MacAddress = d.MacAddress,
                    FirmwareVersion = d.FirmwareVersion,
                    Port = d.Port
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el descubrimiento de dispositivos");
            return new DiscoverDevicesResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
