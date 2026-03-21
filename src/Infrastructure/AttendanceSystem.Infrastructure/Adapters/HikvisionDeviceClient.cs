using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace AttendanceSystem.Infrastructure.Adapters;

public class HikvisionDeviceClient : IDeviceClient
{
    private readonly ILogger<HikvisionDeviceClient> _logger;
    private readonly HttpClient _httpClient;
    private string? _baseUrl;
    private string? _username;
    private string? _password;

    public HikvisionDeviceClient(ILogger<HikvisionDeviceClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<bool> ConnectAsync(string ipAddress, int port, string? username = null, string? password = null, CancellationToken cancellationToken = default)
    {
        _username = username ?? "admin";
        _password = password;
        _baseUrl = $"http://{ipAddress}:{port}";
        
        try
        {
            var info = await GetDeviceInfoAsync(cancellationToken);
            return info != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error conectando a Hikvision en {IpAddress}", ipAddress);
            return false;
        }
    }

    public async Task<IReadOnlyList<RawAttendanceRecord>> GetAttendanceLogsAsync(string deviceId, DateTime? fromDate, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Obteniendo logs de Hikvision para {DeviceId} desde {From} hasta {To}...", deviceId, fromDate, toDate);
        
        var start = fromDate?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.Today.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        var end = toDate?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        var filter = $@"<AcsEventSearchDescription>
            <searchID>{Guid.NewGuid()}</searchID>
            <searchResultPosition>0</searchResultPosition>
            <maxResults>1000</maxResults>
            <startTime>{start}</startTime>
            <endTime>{end}</endTime>
        </AcsEventSearchDescription>";

        try
        {
            var response = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/AcsEvent", filter, cancellationToken);
            if (!response.IsSuccessStatusCode) return new List< RawAttendanceRecord>();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseAcsEvents(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo logs de Hikvision");
            return new List<RawAttendanceRecord>();
        }
    }

    private IReadOnlyList<RawAttendanceRecord> ParseAcsEvents(string xml)
    {
        var records = new List<RawAttendanceRecord>();
        
        // El XML de Hikvision tiene múltiples <AcsEvent> dentro de <AcsEventSearchList>
        var searchPattern = "<AcsEvent>";
        var currentPos = 0;

        while ((currentPos = xml.IndexOf(searchPattern, currentPos)) != -1)
        {
            var endPos = xml.IndexOf("</AcsEvent>", currentPos);
            if (endPos == -1) break;

            var eventXml = xml[currentPos..(endPos + 11)];
            
            var employeeId = ExtractXmlValue(eventXml, "employeeNoString");
            var timeStr = ExtractXmlValue(eventXml, "time");
            var major = ExtractXmlValue(eventXml, "major");
            var minor = ExtractXmlValue(eventXml, "minor");

            // Major 5 y Minor 1 ó 38 suelen ser eventos de acceso/asistencia válidos
            // 1 = Legal Card, 38 = Face Match
            if (!string.IsNullOrEmpty(employeeId) && DateTime.TryParse(timeStr, out var time))
            {
                // Mapear el "minor" a CheckType si es posible
                // Por defecto entrada (0) si no sabemos
                records.Add(new RawAttendanceRecord(employeeId, time, 0, 0, 0));
            }

            currentPos = endPos + 11;
        }

        return records;
    }

    public Task<bool> ClearLogsAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Hikvision no soporta limpieza remota de logs vía ISAPI de forma estándar o requiere privilegios especiales.");
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<DeviceInfoDto?> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendRequestAsync(HttpMethod.Get, "/ISAPI/System/deviceInfo", null, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            // Parsing simple de XML manual para evitar dependencias pesadas
            var deviceName = ExtractXmlValue(xml, "deviceName");
            var serialNumber = ExtractXmlValue(xml, "serialNumber");
            var model = ExtractXmlValue(xml, "model");
            var firmware = ExtractXmlValue(xml, "firmwareVersion");

            return new DeviceInfoDto(
                serialNumber ?? "Unknown",
                deviceName ?? model ?? "Hikvision",
                firmware ?? "Unknown",
                model ?? "Hikvision",
                0, 0, 0, 0, // Counts will be updated elsewhere
                0, 0, 0, 0
            );
        }
        catch
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, string? content = null, CancellationToken cancellationToken = default)
    {
        var url = _baseUrl + path;
        
        // Primera petición sin auth para ver si el servidor desafía con Digest
        var request = new HttpRequestMessage(method, url);
        if (content != null) request.Content = new StringContent(content, System.Text.Encoding.UTF8, "application/xml");
        
        // Nota: Una implementación real de Digest requiere capturar 401, parsear el desafío nonce/realm
        // y reintentar con el header 'Authorization: Digest ...'.
        // Para este MVP, usaremos autenticación básica si el cliente la soporta o simularemos el flujo si es necesario.
        // O mejor, configuramos el HttpClientHandler con credenciales si es posible.
        
        // Intentar con Basic Auth por ahora (muchos Hikvision la permiten si se activa)
        if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
        {
            var authBytes = System.Text.Encoding.ASCII.GetBytes($"{_username}:{_password}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private string? ExtractXmlValue(string xml, string tag)
    {
        var startTag = $"<{tag}>";
        var endTag = $"</{tag}>";
        var start = xml.IndexOf(startTag);
        if (start == -1) return null;
        start += startTag.Length;
        var end = xml.IndexOf(endTag, start);
        if (end == -1) return null;
        return xml[start..end];
    }

    public async Task<IReadOnlyList<DeviceUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var searchXml = $@"<UserInfoSearchCond>
            <searchID>{Guid.NewGuid()}</searchID>
            <maxResults>1000</maxResults>
            <searchResultPosition>0</searchResultPosition>
        </UserInfoSearchCond>";
        
        var faceDict = new Dictionary<string, string>();
        try 
        {
            var faceSearchXml = $@"<FaceInfoSearchCond>
                <searchID>{Guid.NewGuid()}</searchID>
                <maxResults>1000</maxResults>
                <searchResultPosition>0</searchResultPosition>
            </FaceInfoSearchCond>";
            
            var faceResponse = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Face/Search", faceSearchXml, cancellationToken);
            if (faceResponse.IsSuccessStatusCode)
            {
                var faceXml = await faceResponse.Content.ReadAsStringAsync(cancellationToken);
                faceDict = ParseFaces(faceXml);
            }
        }
        catch (Exception ex)
        {
             _logger.LogWarning(ex, "Error buscando rostros/fotos en Hikvision. Continuando sin fotos.");
        }

        try
        {
            var response = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Search", searchXml, cancellationToken);
            if (!response.IsSuccessStatusCode) return new List<DeviceUserDto>();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseUsers(xml, faceDict);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buscando usuarios en Hikvision");
            return new List<DeviceUserDto>();
        }
    }

    private List<DeviceUserDto> ParseUsers(string xml, Dictionary<string, string>? faceDict = null)
    {
        var users = new List<DeviceUserDto>();
        var pattern = "<UserInfo>";
        var pos = 0;

        while ((pos = xml.IndexOf(pattern, pos)) != -1)
        {
            var end = xml.IndexOf("</UserInfo>", pos);
            if (end == -1) break;

            var userXml = xml[pos..(end + 11)];
            var pin = ExtractXmlValue(userXml, "employeeNo");
            var name = ExtractXmlValue(userXml, "name");
            var privilege = ExtractXmlValue(userXml, "userType") == "admin" ? 3 : 0;
            
            string? photo = null;
            if (pin != null && faceDict?.TryGetValue(pin, out photo) == true)
            {
                // Hikvision photo is in faceDict
            }

            if (!string.IsNullOrEmpty(pin))
            {
                users.Add(new DeviceUserDto(pin, name ?? string.Empty, string.Empty, privilege, true, Photo: photo));
            }
            pos = end + 11;
        }
        return users;
    }

    private Dictionary<string, string> ParseFaces(string xml)
    {
        var faceDict = new Dictionary<string, string>();
        var pattern = "<FaceInfo>";
        var pos = 0;

        while ((pos = xml.IndexOf(pattern, pos)) != -1)
        {
            var end = xml.IndexOf("</FaceInfo>", pos);
            if (end == -1) break;

            var faceXml = xml[pos..(end + 11)];
            var pin = ExtractXmlValue(faceXml, "employeeNo");
            var faceData = ExtractXmlValue(faceXml, "faceData"); // This is the Base64 image data

            if (!string.IsNullOrEmpty(pin) && !string.IsNullOrEmpty(faceData))
            {
                faceDict[pin] = faceData;
            }
            pos = end + 11;
        }
        return faceDict;
    }

    public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var deleteXml = $@"<UserInfoDetail>
            <EmployeeNoList>
                <EmployeeNo>
                    <employeeNo>{userId}</employeeNo>
                </EmployeeNo>
            </EmployeeNoList>
        </UserInfoDetail>";

        try
        {
            var response = await SendRequestAsync(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Delete", deleteXml, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando usuario {UserId} en Hikvision", userId);
            return false;
        }
    }

    public Task<bool> DeleteUserFingerprintsAsync(string userId, CancellationToken cancellationToken = default)
    {
        // En ISAPI, borrar huellas es un comando separado pero requiere más complejidad.
        // Por ahora lo dejamos como exitoso o advertimos.
        _logger.LogWarning("Borrado de huellas individual no implementado para Hikvision vía ISAPI aún.");
        return Task.FromResult(true);
    }

    public async Task<bool> ResetToFactorySettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendRequestAsync(HttpMethod.Put, "/ISAPI/System/factoryReset", null, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
    {
        var timeXml = $@"<Time>
            <timeMode>manual</timeMode>
            <localTime>{dateTime:yyyy-MM-ddTHH:mm:ss}</localTime>
        </Time>";

        try
        {
            var response = await SendRequestAsync(HttpMethod.Put, "/ISAPI/System/time", timeXml, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetUserAsync(DeviceUserDto user, CancellationToken cancellationToken = default)
    {
        var userXml = $@"<UserInfo>
            <employeeNo>{user.UserId}</employeeNo>
            <name>{user.Name}</name>
            <userType>{(user.Privilege >= 3 ? "admin" : "normal")}</userType>
            <Valid>
                <beginTime>2023-01-01T00:00:00</beginTime>
                <endTime>2099-12-31T23:59:59</endTime>
                <timeType>local</timeType>
            </Valid>
            <doorRight>1</doorRight>
            <RightPlan>
                <doorNo>1</doorNo>
                <planTemplateNo>1</planTemplateNo>
            </RightPlan>
        </UserInfo>";

        try
        {
            // Record create/update
            var response = await SendRequestAsync(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Record", userXml, cancellationToken);
            var success = response.IsSuccessStatusCode;

            if (success && !string.IsNullOrWhiteSpace(user.Photo))
            {
                var faceXml = $@"<FaceInfoRecord>
                    <employeeNo>{user.UserId}</employeeNo>
                    <faceData>{user.Photo}</faceData>
                </FaceInfoRecord>";
                
                try 
                {
                    await SendRequestAsync(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Face/Record", faceXml, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error enviando foto de usuario {UserId} a Hikvision", user.UserId);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando usuario {UserId} a Hikvision", user.UserId);
            return false;
        }
    }
}
