using Grpc.Core;
using AttendanceSystem.ZKTeco.Grpc;

using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.ZKTeco.Service.Services;

public class ZKTecoGrpcService : AttendanceSystem.ZKTeco.Grpc.ZKTecoService.ZKTecoServiceBase
{
    private readonly ILogger<ZKTecoGrpcService> _logger;
    // We instantiate the client directly or via DI if possible, but since ZKTecoDeviceClient lives in Infrastructure.ZKTeco...
    // Let's assume DI is set up in Program.cs to inject IZKTecoDeviceClient implementation.
    private readonly IZKTecoDeviceClient _zkClient;

    public ZKTecoGrpcService(
        ILogger<ZKTecoGrpcService> logger,
        IZKTecoDeviceClient zkClient)
    {
        _logger = logger;
        _zkClient = zkClient;
    }

    public override async Task<ConnectDeviceResponse> ConnectDevice(ConnectDeviceRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Conectando a {IpAddress}:{Port}...", request.IpAddress, request.Port);
        
        try
        {
            var connected = await _zkClient.ConnectAsync(request.IpAddress, request.Port, context.CancellationToken);
            
            return new ConnectDeviceResponse
            {
                Success = connected,
                Message = connected ? "Conexión exitosa" : "Error al conectar con el dispositivo",
                DeviceSerialNumber = "UNKNOWN" // TODO: Get serial if needed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al conectar con dispositivo");
            return new ConnectDeviceResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<GetAttendanceLogsResponse> GetAttendanceLogs(GetAttendanceLogsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Obteniendo logs de {DeviceId}...", request.DeviceId);
        
        try
        {
            DateTime? fromDate = null;
            if (!string.IsNullOrEmpty(request.FromDate) && DateTime.TryParse(request.FromDate, out var fDate))
            {
                fromDate = fDate;
            }

            DateTime? toDate = null;
            if (!string.IsNullOrEmpty(request.ToDate) && DateTime.TryParse(request.ToDate, out var tDate))
            {
                toDate = tDate;
            }

            var logs = await _zkClient.GetAttendanceLogsAsync(request.DeviceId, fromDate, toDate, context.CancellationToken);

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
            _logger.LogError(ex, "Error obteniendo logs");
            return new GetAttendanceLogsResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<ClearDeviceLogsResponse> ClearDeviceLogs(ClearDeviceLogsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Limpiando logs de {DeviceId}...", request.DeviceId);
        
        try
        {
            var success = await _zkClient.ClearLogsAsync(request.DeviceId, context.CancellationToken);
            return new ClearDeviceLogsResponse
            {
                Success = success,
                Message = success ? "Logs limpiados correctamente" : "Fallo al limpiar logs"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error limpiando logs");
            return new ClearDeviceLogsResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<DisconnectDeviceResponse> DisconnectDevice(DisconnectDeviceRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Desconectando dispositivo...");
        try
        {
            await _zkClient.DisconnectAsync(context.CancellationToken);
            return new DisconnectDeviceResponse { Success = true, Message = "Desconectado" };
        }
        catch (Exception ex)
        {
            return new DisconnectDeviceResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<GetDeviceInfoResponse> GetDeviceInfo(GetDeviceInfoRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Obteniendo información del dispositivo...");
        
        try
        {
            var deviceInfo = await _zkClient.GetDeviceInfoAsync(context.CancellationToken);
            
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
                    AttendanceRecordCapacity = deviceInfo.AttendanceRecordCapacity
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener información del dispositivo");
            return new GetDeviceInfoResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    public override async Task<DeleteEmployeeResponse> DeleteEmployee(DeleteEmployeeRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Eliminando empleado {EmployeeId} de {DeviceId}...", request.EmployeeId, request.DeviceId);
        try
        {
            var success = await _zkClient.DeleteUserAsync(request.EmployeeId, context.CancellationToken);
            return new DeleteEmployeeResponse 
            { 
                Success = success, 
                Message = success ? "Empleado eliminado" : "No se pudo eliminar el empleado" 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando empleado");
            return new DeleteEmployeeResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<GetAllUsersResponse> GetAllUsers(GetAllUsersRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Obteniendo usuarios de {DeviceId}...", request.DeviceId);
        try
        {
            var users = await _zkClient.GetAllUsersAsync(context.CancellationToken);
            var response = new GetAllUsersResponse
            {
                Success = true,
                Message = $"Se obtuvieron {users.Count} usuarios"
            };
            
            foreach(var u in users)
            {
                var protoUser = new AttendanceSystem.ZKTeco.Grpc.DeviceUser
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Password = u.Password,
                    Privilege = u.Privilege,
                    Enabled = u.Enabled,
                    CardNumber = u.CardNumber ?? "",
                    FaceTemplate = u.FaceTemplate ?? ""
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
        catch(Exception ex)
        {
             _logger.LogError(ex, "Error obteniendo usuarios");
             return new GetAllUsersResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<DeleteUserFingerprintsResponse> DeleteUserFingerprints(DeleteUserFingerprintsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Eliminando huellas de {UserId} en {DeviceId}...", request.UserId, request.DeviceId);
        try
        {
            var success = await _zkClient.DeleteUserFingerprintsAsync(request.UserId, context.CancellationToken);
            return new DeleteUserFingerprintsResponse
            {
                Success = success,
                Message = success ? "Huellas eliminadas" : "No se pudieron eliminar las huellas"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando huellas");
            return new DeleteUserFingerprintsResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<ResetToFactorySettingsResponse> ResetToFactorySettings(ResetToFactorySettingsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Restableciendo valores de fábrica en {DeviceId}...", request.DeviceId);
        try
        {
            var success = await _zkClient.ResetToFactorySettingsAsync(context.CancellationToken);
            return new ResetToFactorySettingsResponse
            {
                Success = success,
                Message = success ? "Restablecimiento exitoso (Datos borrados)" : "Fallo el restablecimiento"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restableciendo valores de fábrica");
            return new ResetToFactorySettingsResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<SetDeviceTimeResponse> SetDeviceTime(SetDeviceTimeRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Configurando hora en {DeviceId}...", request.DeviceId);
        try
        {
            if(!DateTime.TryParse(request.DateTime, out var dt))
            {
                 return new SetDeviceTimeResponse { Success = false, Message = "Formato de fecha inválido" };
            }

            var success = await _zkClient.SetDeviceTimeAsync(dt, context.CancellationToken);
             return new SetDeviceTimeResponse
            {
                Success = success,
                Message = success ? "Hora configurada exitosamente" : "Fallo al configurar hora"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configurando hora");
             return new SetDeviceTimeResponse { Success = false, Message = ex.Message };
        }
    }

    public override async Task<RegisterEmployeeResponse> RegisterEmployee(RegisterEmployeeRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Registrando usuario {UserId} ({Name}) en {DeviceId}...", request.EmployeeId, request.Name, request.DeviceId);
        try
        {
             var userDto = new Application.DTOs.DeviceUserDto(
                request.EmployeeId,
                request.Name,
                request.Password,
                request.Privilege,
                request.Enabled,
                string.IsNullOrEmpty(request.CardNumber) ? null : request.CardNumber,
                request.Fingerprints.Select(f => new Application.DTOs.DeviceFingerprintDto(f.FingerIndex, f.TemplateData)).ToList(),
                string.IsNullOrEmpty(request.FaceTemplate) ? null : request.FaceTemplate);

            var success = await _zkClient.SetUserAsync(userDto, context.CancellationToken);
            
            return new RegisterEmployeeResponse
            {
                Success = success,
                Message = success ? "Usuario registrado/actualizado correctamnte" : "Fallo al registrar usuario"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registrando usuario {UserId}", request.EmployeeId);
            return new RegisterEmployeeResponse { Success = false, Message = ex.Message };
        }
    }
}
