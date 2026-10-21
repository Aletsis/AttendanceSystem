using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;

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

    public async Task<DeviceLogsResult> GetAttendanceLogsAsync(string deviceId, DateTime? fromDate, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Obteniendo logs de Hikvision para {DeviceId} desde {From} hasta {To}...", deviceId, fromDate, toDate);

        var start = fromDate?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.Today.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        var end = toDate?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        var records = new List<RawAttendanceRecord>();
        int searchPosition = 0;
        const int pageSize = 50;
        int totalMatches = 0;
        var searchId = Guid.NewGuid().ToString();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filter = $@"<AcsEventSearchDescription>
            <searchID>{searchId}</searchID>
            <searchResultPosition>{searchPosition}</searchResultPosition>
            <maxResults>{pageSize}</maxResults>
            <startTime>{start}</startTime>
            <endTime>{end}</endTime>
        </AcsEventSearchDescription>";

                var response = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/AcsEvent", filter, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorMsg = $"Error HTTP {statusCode} ({response.ReasonPhrase}) al consultar eventos en posición {searchPosition}";
                    _logger.LogError("Hikvision {DeviceId}: {Error}", deviceId, errorMsg);

                    if (records.Count > 0)
                    {
                        return DeviceLogsResult.Partial(records, errorMsg, totalMatches > 0 ? totalMatches : records.Count);
                    }
                    return DeviceLogsResult.Failed(errorMsg);
                }

                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                var pageRecords = ParseAcsEventsXml(xml, out var statusStr, out var numOfMatches, out var reportedTotal);

                if (reportedTotal > 0)
                {
                    totalMatches = reportedTotal;
                }

                records.AddRange(pageRecords);

                if (string.Equals(statusStr, "NO MATCH", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (numOfMatches <= 0 || pageRecords.Count == 0)
                {
                    break;
                }

                searchPosition += numOfMatches;

                if (totalMatches > 0 && searchPosition >= totalMatches)
                {
                    break;
                }

                if (!string.IsNullOrEmpty(statusStr) &&
                    !string.Equals(statusStr, "MORE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(statusStr, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            if (records.Count == 0)
            {
                return DeviceLogsResult.Empty(totalMatches);
            }

            return DeviceLogsResult.Success(records, totalMatches > 0 ? totalMatches : records.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Descarga cancelada para dispositivo Hikvision {DeviceId}", deviceId);
            return DeviceLogsResult.Cancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción obteniendo logs de Hikvision para {DeviceId}", deviceId);
            if (records.Count > 0)
            {
                return DeviceLogsResult.Partial(records, ex.Message, totalMatches > 0 ? totalMatches : records.Count);
            }
            return DeviceLogsResult.Failed(ex.Message);
        }
    }

    private static IReadOnlyList<RawAttendanceRecord> ParseAcsEventsXml(
        string xml,
        out string? statusStr,
        out int numOfMatches,
        out int totalMatches)
    {
        statusStr = null;
        numOfMatches = 0;
        totalMatches = 0;
        var records = new List<RawAttendanceRecord>();

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return records;

            statusStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "responseStatusStrg")?.Value;
            var numStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "numOfMatches")?.Value;
            var totStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "totalMatches")?.Value;

            if (int.TryParse(numStr, out var n)) numOfMatches = n;
            if (int.TryParse(totStr, out var t)) totalMatches = t;

            foreach (var eventElem in root.Descendants().Where(e => e.Name.LocalName == "AcsEvent"))
            {
                var employeeId = eventElem.Elements().FirstOrDefault(e => e.Name.LocalName == "employeeNoString")?.Value;
                var timeStr = eventElem.Elements().FirstOrDefault(e => e.Name.LocalName == "time")?.Value;
                var minorStr = eventElem.Elements().FirstOrDefault(e => e.Name.LocalName == "minor")?.Value;

                if (string.IsNullOrEmpty(employeeId)) continue;

                DateTime time;
                if (DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                {
                    time = dto.UtcDateTime;
                }
                else if (DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                {
                    time = dt;
                }
                else
                {
                    continue;
                }

                int inOutMode = 0;
                records.Add(new RawAttendanceRecord(employeeId, time, 0, inOutMode, 0));
            }
        }
        catch
        {
            // Fallback en caso de XML malformado
        }

        return records;
    }

    public Task<bool> ClearLogsAsync(
        string deviceId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Hikvision no soporta limpieza de logs vía ISAPI de forma estándar. Operación rechazada de forma segura.");
        return Task.FromResult(false);
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
        var faceDict = new Dictionary<string, string>();
        try
        {
            int facePosition = 0;
            const int facePageSize = 50;
            var faceSearchId = Guid.NewGuid().ToString();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var faceSearchXml = $@"<FaceInfoSearchCond>
                    <searchID>{faceSearchId}</searchID>
                    <maxResults>{facePageSize}</maxResults>
                    <searchResultPosition>{facePosition}</searchResultPosition>
                </FaceInfoSearchCond>";

                var faceResponse = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Face/Search", faceSearchXml, cancellationToken);
                if (!faceResponse.IsSuccessStatusCode) break;

                var faceXml = await faceResponse.Content.ReadAsStringAsync(cancellationToken);
                var (pageFaces, statusStr, numMatches, totalMatches) = ParseFacesXml(faceXml);
                foreach (var kvp in pageFaces)
                {
                    faceDict[kvp.Key] = kvp.Value;
                }

                if (numMatches <= 0 || pageFaces.Count == 0) break;
                facePosition += numMatches;
                if (totalMatches > 0 && facePosition >= totalMatches) break;
                if (!string.IsNullOrEmpty(statusStr) &&
                    !string.Equals(statusStr, "MORE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(statusStr, "OK", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error buscando rostros/fotos en Hikvision. Continuando sin fotos.");
        }

        var users = new List<DeviceUserDto>();
        try
        {
            int userPosition = 0;
            const int userPageSize = 50;
            var userSearchId = Guid.NewGuid().ToString();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var searchXml = $@"<UserInfoSearchCond>
                    <searchID>{userSearchId}</searchID>
                    <maxResults>{userPageSize}</maxResults>
                    <searchResultPosition>{userPosition}</searchResultPosition>
                </UserInfoSearchCond>";

                var response = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Search", searchXml, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Error HTTP al consultar usuarios en posición {Pos} de Hikvision", userPosition);
                    break;
                }

                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                var (pageUsers, statusStr, numMatches, totalMatches) = ParseUsersXml(xml, faceDict);
                users.AddRange(pageUsers);

                if (numMatches <= 0 || pageUsers.Count == 0) break;
                userPosition += numMatches;
                if (totalMatches > 0 && userPosition >= totalMatches) break;
                if (!string.IsNullOrEmpty(statusStr) &&
                    !string.Equals(statusStr, "MORE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(statusStr, "OK", StringComparison.OrdinalIgnoreCase)) break;
            }

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buscando usuarios en Hikvision");
            return users;
        }
    }

    private static (List<DeviceUserDto> Users, string? StatusStr, int NumMatches, int TotalMatches) ParseUsersXml(string xml, Dictionary<string, string>? faceDict = null)
    {
        var users = new List<DeviceUserDto>();
        string? statusStr = null;
        int numMatches = 0;
        int totalMatches = 0;

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return (users, null, 0, 0);

            statusStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "responseStatusStrg")?.Value;
            var numStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "numOfMatches")?.Value;
            var totStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "totalMatches")?.Value;

            if (int.TryParse(numStr, out var n)) numMatches = n;
            if (int.TryParse(totStr, out var t)) totalMatches = t;

            foreach (var userElem in root.Descendants().Where(e => e.Name.LocalName == "UserInfo"))
            {
                var pin = userElem.Elements().FirstOrDefault(e => e.Name.LocalName == "employeeNo")?.Value;
                var name = userElem.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;
                var userType = userElem.Elements().FirstOrDefault(e => e.Name.LocalName == "userType")?.Value;
                var privilege = (int)DevicePrivilegeMapper.MapFromHikvisionUserType(userType);

                string? photo = null;
                if (pin != null && faceDict?.TryGetValue(pin, out photo) == true)
                {
                    // Photo exists
                }

                if (!string.IsNullOrEmpty(pin))
                {
                    users.Add(new DeviceUserDto(pin, name ?? string.Empty, string.Empty, privilege, true, Photo: photo));
                }
            }
        }
        catch
        {
        }

        return (users, statusStr, numMatches, totalMatches);
    }

    private static (Dictionary<string, string> Faces, string? StatusStr, int NumMatches, int TotalMatches) ParseFacesXml(string xml)
    {
        var faceDict = new Dictionary<string, string>();
        string? statusStr = null;
        int numMatches = 0;
        int totalMatches = 0;

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return (faceDict, null, 0, 0);

            statusStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "responseStatusStrg")?.Value;
            var numStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "numOfMatches")?.Value;
            var totStr = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "totalMatches")?.Value;

            if (int.TryParse(numStr, out var n)) numMatches = n;
            if (int.TryParse(totStr, out var t)) totalMatches = t;

            foreach (var faceElem in root.Descendants().Where(e => e.Name.LocalName == "FaceInfo"))
            {
                var pin = faceElem.Elements().FirstOrDefault(e => e.Name.LocalName == "employeeNo")?.Value;
                var faceData = faceElem.Elements().FirstOrDefault(e => e.Name.LocalName == "faceData")?.Value;

                if (!string.IsNullOrEmpty(pin) && !string.IsNullOrEmpty(faceData))
                {
                    faceDict[pin] = faceData;
                }
            }
        }
        catch
        {
        }

        return (faceDict, statusStr, numMatches, totalMatches);
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

    public Task<bool> SetDeviceTimeAsync(DateTime dateTime, CancellationToken cancellationToken = default)
        => PutTimeAsync(dateTime, null, cancellationToken);

    public Task<bool> SetDeviceClockAsync(DateTime civilTime, TimeSpan utcOffset, CancellationToken cancellationToken = default)
        => PutTimeAsync(civilTime, utcOffset, cancellationToken);

    public async Task<DateTime?> GetDeviceTimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendRequestAsync(HttpMethod.Get, "/ISAPI/System/time", null, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var match = System.Text.RegularExpressions.Regex.Match(xml, @"<localTime>(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})");
            // La hora local del equipo, sin su desfase: es la hora civil que marca el reloj.
            return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var time) ? DateTime.SpecifyKind(time, DateTimeKind.Unspecified) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> PutTimeAsync(DateTime dateTime, TimeSpan? utcOffset, CancellationToken cancellationToken)
    {
        // ISAPI expresa la zona en notación POSIX: el signo es el inverso del desfase (UTC-6 → "CST+6:00:00").
        var zone = utcOffset is { } offset
            ? $"<timeZone>CST{(offset <= TimeSpan.Zero ? "+" : "-")}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):00}:00</timeZone>"
            : string.Empty;
        var timeXml = $@"<Time>
            <timeMode>manual</timeMode>
            <localTime>{dateTime:yyyy-MM-ddTHH:mm:ss}</localTime>
            {zone}
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
        var userType = DevicePrivilegeMapper.MapToHikvisionUserType((DevicePrivilege)user.Privilege);
        var userXml = $@"<UserInfo>
            <employeeNo>{user.UserId}</employeeNo>
            <name>{user.Name}</name>
            <userType>{userType}</userType>
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

    public async Task<DeviceUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var searchXml = $@"<UserInfoSearchCond>
            <searchID>{Guid.NewGuid()}</searchID>
            <maxResults>1</maxResults>
            <searchResultPosition>0</searchResultPosition>
            <EmployeeNoList>
                <EmployeeNo>
                    <employeeNo>{userId}</employeeNo>
                </EmployeeNo>
            </EmployeeNoList>
        </UserInfoSearchCond>";

        try
        {
            var response = await SendRequestAsync(HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Search", searchXml, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var (users, _, _, _) = ParseUsersXml(xml);
            return users.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buscando usuario {UserId} en Hikvision", userId);
            return null;
        }
    }
}
