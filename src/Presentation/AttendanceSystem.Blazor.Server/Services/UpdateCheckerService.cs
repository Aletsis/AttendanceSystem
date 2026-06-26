using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Blazor.Server.Services
{
    public class UpdateCheckerService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UpdateCheckerService> _logger;
        
        private UpdateCheckResult? _cachedResult;
        private DateTime? _lastCheckTime;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(12);

        public UpdateCheckerService(IHttpClientFactory httpClientFactory, ILogger<UpdateCheckerService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
        {
            if (!force && _cachedResult != null && _lastCheckTime.HasValue && 
                (DateTime.UtcNow - _lastCheckTime.Value) < _cacheDuration)
            {
                _logger.LogInformation("Retornando resultado de verificación de actualización desde caché.");
                return _cachedResult;
            }

            var currentVersion = typeof(Program).Assembly.GetName().Version ?? new Version(1, 0, 0);
            
            try
            {
                _logger.LogInformation("Consultando la última versión en GitHub Releases...");
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AttendanceSystem-UpdateChecker");

                var response = await httpClient.GetFromJsonAsync<GitHubRelease>(
                    "https://api.github.com/repos/Aletsis/AttendanceSystem/releases/latest");

                if (response == null || string.IsNullOrEmpty(response.TagName))
                {
                    _logger.LogWarning("No se recibió una respuesta válida de la API de GitHub.");
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = false,
                        CurrentVersion = currentVersion.ToString(),
                        LatestVersion = "Desconocida",
                        ReleaseNotes = "No se pudieron obtener las notas del release.",
                        DownloadUrl = string.Empty
                    };
                }

                // Limpiar el prefijo 'v' si existe (ej. v2.1.3 -> 2.1.3)
                var cleanTagName = response.TagName.TrimStart('v', 'V');
                if (Version.TryParse(cleanTagName, out var latestVersion))
                {
                    bool isAvailable = latestVersion > currentVersion;
                    
                    // Buscar el instalador .exe en los assets
                    string downloadUrl = response.HtmlUrl; // Fallback
                    foreach (var asset in response.Assets)
                    {
                        if (asset.Name.Equals("AttendanceSystem_Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.BrowserDownloadUrl;
                            break;
                        }
                    }

                    _cachedResult = new UpdateCheckResult
                    {
                        IsUpdateAvailable = isAvailable,
                        CurrentVersion = currentVersion.ToString(3), // ej: 2.1.3
                        LatestVersion = latestVersion.ToString(3),
                        ReleaseNotes = response.Body,
                        DownloadUrl = downloadUrl
                    };
                    _lastCheckTime = DateTime.UtcNow;

                    _logger.LogInformation("Verificación de actualización completada. Disponible: {IsAvailable}, Local: {Local}, Remota: {Remote}", 
                        isAvailable, currentVersion, latestVersion);
                    
                    return _cachedResult;
                }
                else
                {
                    _logger.LogWarning("No se pudo parsear la versión de la etiqueta: {TagName}", response.TagName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar actualizaciones desde GitHub.");
            }

            // En caso de error, retornar que no hay actualización pero mantener la versión local
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = currentVersion.ToString(3),
                LatestVersion = "Error al conectar",
                ReleaseNotes = "No se pudo conectar al servidor de actualizaciones.",
                DownloadUrl = string.Empty
            };
        }
    }

    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }

    internal class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    internal class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
