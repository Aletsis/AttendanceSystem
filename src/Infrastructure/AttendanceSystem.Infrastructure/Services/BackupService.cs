using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services;

public class BackupService : IBackupService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupService> _logger;
    private readonly ISystemConfigurationRepository _systemConfigRepository;
    private readonly string _postgresHost = string.Empty;
    private readonly string _postgresPort = string.Empty;
    private readonly string _postgresDatabase = string.Empty;
    private readonly string _postgresUser = string.Empty;
    private readonly string _postgresPassword = string.Empty;

    public BackupService(
        IConfiguration configuration,
        ILogger<BackupService> logger,
        ISystemConfigurationRepository systemConfigRepository)
    {
        _configuration = configuration;
        _logger = logger;
        _systemConfigRepository = systemConfigRepository;

        // Parsear connection string de PostgreSQL
        var connectionString = configuration.GetConnectionString("AttendanceDb")
            ?? throw new InvalidOperationException("Connection string 'AttendanceDb' no encontrada en la configuración.");


        var connParams = ParseConnectionString(connectionString);
        _postgresHost = GetValueWithAliases(connParams, "Host", "Server", "Data Source") ?? "";
        _postgresPort = GetValueWithAliases(connParams, "Port") ?? "5432";
        _postgresDatabase = GetValueWithAliases(connParams, "Database", "Initial Catalog") ?? "";
        _postgresUser = GetValueWithAliases(connParams, "Username", "User Id", "UserId", "User") ?? "";
        _postgresPassword = GetValueWithAliases(connParams, "Password", "Pwd") ?? "";

        if (string.IsNullOrEmpty(_postgresHost) || string.IsNullOrEmpty(_postgresDatabase) || string.IsNullOrEmpty(_postgresUser))
        {
            _logger.LogError("Faltan parámetros críticos en la cadena de conexión para el respaldo: Host={Host}, DB={DB}, User={User}",
                _postgresHost ?? "NULO", _postgresDatabase ?? "NULO", _postgresUser ?? "NULO");
        }
    }

    private string? GetValueWithAliases(Dictionary<string, string> dict, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (dict.TryGetValue(alias, out var value)) return value;
        }
        return null;
    }

    private async Task<string> GetBackupDirectoryAsync()
    {
        _logger.LogInformation("Obteniendo directorio de respaldos desde configuración del sistema...");
        var config = await _systemConfigRepository.GetConfigurationAsync();
        var configuredDir = config?.BackupDirectory;

        string backupDir;
        if (string.IsNullOrWhiteSpace(configuredDir))
        {
            backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
        }
        else if (Path.IsPathRooted(configuredDir))
        {
            backupDir = configuredDir;
        }
        else
        {
            backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredDir);
        }

        _logger.LogInformation("Directorio de respaldos configurado: {BackupDirectory}", backupDir);

        if (!Directory.Exists(backupDir))
        {
            _logger.LogInformation("El directorio no existe. Creando: {BackupDirectory}", backupDir);
            Directory.CreateDirectory(backupDir);
            _logger.LogInformation("Directorio creado exitosamente");
        }

        return backupDir;
    }

    public async Task<BackupResultDto> CreateFullBackupAsync(string? description = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== INICIANDO RESPALDO COMPLETO ===");
        _logger.LogInformation("Descripción: {Description}", description ?? "Sin descripción");

        string? tempDir = null;

        try
        {
            var backupDirectory = await GetBackupDirectoryAsync();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"Full_Backup_{timestamp}.zip";
            var backupFilePath = Path.Combine(backupDirectory, backupFileName);
            tempDir = Path.Combine(Path.GetTempPath(), $"AttendanceBackup_{timestamp}");

            _logger.LogInformation("Archivo de respaldo: {BackupFilePath}", backupFilePath);
            _logger.LogInformation("Directorio temporal: {TempDir}", tempDir);

            _logger.LogInformation("Creando directorio temporal...");
            Directory.CreateDirectory(tempDir);
            _logger.LogInformation("Directorio temporal creado exitosamente");

            try
            {
                // 1. Respaldar base de datos
                _logger.LogInformation("[1/4] Creando respaldo de base de datos...");
                var dbBackupFile = Path.Combine(tempDir, "database.backup");
                _logger.LogInformation("Archivo de BD temporal: {DbBackupFile}", dbBackupFile);
                var dbResult = await CreateDatabaseBackupFileAsync(dbBackupFile, cancellationToken);

                if (!dbResult.Success)
                {
                    _logger.LogError("Fallo al crear respaldo de base de datos: {Message}", dbResult.Message);
                    return new BackupResultDto
                    {
                        Success = false,
                        Message = dbResult.Message
                    };
                }

                _logger.LogInformation("Respaldo de base de datos creado exitosamente");

                // 2. Copiar archivos de configuración
                _logger.LogInformation("[2/4] Copiando archivos de configuración...");
                var configDir = Path.Combine(tempDir, "config");
                Directory.CreateDirectory(configDir);
                _logger.LogInformation("Directorio de configuración creado: {ConfigDir}", configDir);

                var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                _logger.LogInformation("Buscando appsettings.json en: {AppSettingsPath}", appSettingsPath);

                if (File.Exists(appSettingsPath))
                {
                    var destPath = Path.Combine(configDir, "appsettings.json");
                    File.Copy(appSettingsPath, destPath);
                    _logger.LogInformation("appsettings.json copiado exitosamente");
                }
                else
                {
                    _logger.LogWarning("appsettings.json no encontrado en la ubicación esperada");
                }

                // 3. Crear metadata del respaldo
                _logger.LogInformation("[3/4] Creando metadata del respaldo...");
                var metadata = new BackupMetadata
                {
                    BackupType = "Full",
                    Description = description ?? "Respaldo completo del sistema",
                    CreatedAt = DateTime.Now,
                    DatabaseName = _postgresDatabase,
                    Version = "1.0"
                };

                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                var metadataPath = Path.Combine(tempDir, "metadata.json");
                await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
                _logger.LogInformation("Metadata creada exitosamente");

                // 4. Comprimir todo
                _logger.LogInformation("[4/4] Comprimiendo respaldo...");
                _logger.LogInformation("Origen: {TempDir}", tempDir);
                _logger.LogInformation("Destino: {BackupFilePath}", backupFilePath);

                try
                {
                    ZipFile.CreateFromDirectory(tempDir, backupFilePath, CompressionLevel.Optimal, false);
                    _logger.LogInformation("Compresión completada exitosamente");
                }
                catch (Exception zipEx)
                {
                    _logger.LogError(zipEx, "Error al comprimir el respaldo");
                    throw new InvalidOperationException($"Error al crear archivo ZIP: {zipEx.Message}", zipEx);
                }

                var fileInfo = new FileInfo(backupFilePath);
                _logger.LogInformation("Tamaño del archivo: {SizeBytes} bytes ({SizeMB:F2} MB)", fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);

                _logger.LogInformation("=== RESPALDO COMPLETO FINALIZADO EXITOSAMENTE ===");
                _logger.LogInformation("Archivo: {FileName}", fileInfo.Name);
                _logger.LogInformation("Ubicación: {FilePath}", backupFilePath);

                return new BackupResultDto
                {
                    Success = true,
                    Message = "Respaldo completo creado exitosamente",
                    BackupFilePath = backupFilePath,
                    SizeInBytes = fileInfo.Length,
                    CreatedAt = DateTime.Now
                };
            }
            finally
            {
                // Limpiar directorio temporal
                if (tempDir != null)
                {
                    _logger.LogInformation("Limpiando directorio temporal...");
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                        _logger.LogInformation("Directorio temporal eliminado: {TempDir}", tempDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear respaldo completo");
            return new BackupResultDto
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public async Task<BackupResultDto> CreateDatabaseBackupAsync(string? description = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var backupDirectory = await GetBackupDirectoryAsync();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"DB_Backup_{timestamp}.backup";
            var backupFilePath = Path.Combine(backupDirectory, backupFileName);

            _logger.LogInformation("Creando respaldo de base de datos...");
            var result = await CreateDatabaseBackupFileAsync(backupFilePath, cancellationToken);

            if (!result.Success)
            {
                return new BackupResultDto
                {
                    Success = false,
                    Message = result.Message
                };
            }

            var fileInfo = new FileInfo(backupFilePath);

            return new BackupResultDto
            {
                Success = true,
                Message = "Respaldo de base de datos creado exitosamente",
                BackupFilePath = backupFilePath,
                SizeInBytes = fileInfo.Length,
                CreatedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear respaldo de base de datos");
            return new BackupResultDto
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public async Task<RestoreResultDto> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== INICIANDO RESTAURACIÓN DE RESPALDO ===");
        _logger.LogInformation("Archivo de respaldo: {BackupFilePath}", backupFilePath);

        try
        {
            if (!File.Exists(backupFilePath))
            {
                _logger.LogError("El archivo de respaldo no existe: {BackupFilePath}", backupFilePath);
                return new RestoreResultDto
                {
                    Success = false,
                    Message = "El archivo de respaldo no existe"
                };
            }

            var fileInfo = new FileInfo(backupFilePath);
            _logger.LogInformation("Tamaño del archivo: {SizeBytes} bytes ({SizeMB:F2} MB)", fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);

            var tempDir = Path.Combine(Path.GetTempPath(), $"AttendanceRestore_{DateTime.Now:yyyyMMdd_HHmmss}");
            _logger.LogInformation("Directorio temporal: {TempDir}", tempDir);
            Directory.CreateDirectory(tempDir);
            _logger.LogInformation("Directorio temporal creado");

            // Determinar tipo de respaldo
            var isZipFile = backupFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Tipo de respaldo: {BackupType}", isZipFile ? "Completo (ZIP)" : "Solo Base de Datos");

            // 0. Crear Snapshot de Seguridad previo antes de tocar la base de datos
            string? safetyBackupFile = null;
            bool safetyBackupCreated = false;

            try
            {
                var backupDirectory = await GetBackupDirectoryAsync();
                safetyBackupFile = Path.Combine(backupDirectory, $".safety_snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.backup");
                _logger.LogInformation("Generando snapshot de seguridad previo en: {SafetyBackupFile}", safetyBackupFile);
                var safetyResult = await CreateDatabaseBackupFileAsync(safetyBackupFile, cancellationToken);
                if (safetyResult.Success)
                {
                    safetyBackupCreated = true;
                    _logger.LogInformation("Snapshot de seguridad previo creado exitosamente.");
                }
                else
                {
                    _logger.LogWarning("No se pudo generar el snapshot de seguridad previo: {Message}. Se continuará con precaución.", safetyResult.Message);
                }
            }
            catch (Exception snapEx)
            {
                _logger.LogWarning(snapEx, "Excepción al intentar crear snapshot de seguridad previo");
            }

            try
            {
                (bool Success, string Message) dbRestoreResult;

                if (isZipFile)
                {
                    // Respaldo completo
                    _logger.LogInformation("[1/3] Extrayendo respaldo completo...");
                    ZipFile.ExtractToDirectory(backupFilePath, tempDir);
                    _logger.LogInformation("Extracción completada");

                    // Leer metadata
                    var metadataPath = Path.Combine(tempDir, "metadata.json");
                    if (File.Exists(metadataPath))
                    {
                        _logger.LogInformation("[2/3] Leyendo metadata del respaldo...");
                        var metadataJson = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                        var metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);
                        _logger.LogInformation("Metadata:");
                        _logger.LogInformation("  Tipo: {BackupType}", metadata?.BackupType);
                        _logger.LogInformation("  Descripción: {Description}", metadata?.Description);
                        _logger.LogInformation("  Fecha de creación: {CreatedAt}", metadata?.CreatedAt);
                        _logger.LogInformation("  Base de datos: {DatabaseName}", metadata?.DatabaseName);
                    }

                    // Restaurar base de datos
                    _logger.LogInformation("[3/3] Restaurando base de datos...");
                    var dbBackupFile = Path.Combine(tempDir, "database.backup");
                    if (File.Exists(dbBackupFile))
                    {
                        var dbFileInfo = new FileInfo(dbBackupFile);
                        _logger.LogInformation("Archivo de BD: {DbBackupFile} ({SizeMB:F2} MB)", dbBackupFile, dbFileInfo.Length / 1024.0 / 1024.0);

                        dbRestoreResult = await RestoreDatabaseFromFileAsync(dbBackupFile, cancellationToken);
                    }
                    else
                    {
                        _logger.LogError("No se encontró el archivo database.backup en el respaldo");
                        dbRestoreResult = (false, "El respaldo no contiene un archivo de base de datos válido");
                    }

                    // Restaurar configuración (opcional - requiere confirmación manual)
                    var configDir = Path.Combine(tempDir, "config");
                    if (Directory.Exists(configDir))
                    {
                        _logger.LogInformation("Archivos de configuración disponibles en: {ConfigDir}", configDir);
                        _logger.LogInformation("NOTA: Los archivos de configuración NO se restauran automáticamente");
                    }
                }
                else
                {
                    // Respaldo solo de base de datos
                    _logger.LogInformation("[1/1] Restaurando base de datos desde archivo .backup...");
                    dbRestoreResult = await RestoreDatabaseFromFileAsync(backupFilePath, cancellationToken);
                }

                if (!dbRestoreResult.Success)
                {
                    _logger.LogError("Falló la restauración de la base de datos: {Message}", dbRestoreResult.Message);

                    // Si falló y tenemos snapshot de seguridad, revertir la base de datos
                    if (safetyBackupCreated && !string.IsNullOrEmpty(safetyBackupFile) && File.Exists(safetyBackupFile))
                    {
                        _logger.LogWarning("Iniciando reversión automática (Rollback) al estado previo usando el snapshot de seguridad...");
                        try
                        {
                            var rollbackResult = await RestoreDatabaseFromFileAsync(safetyBackupFile, cancellationToken);
                            if (rollbackResult.Success)
                            {
                                _logger.LogInformation("Rollback completado con éxito. La base de datos se mantiene en su estado previo original.");
                                return new RestoreResultDto
                                {
                                    Success = false,
                                    Message = $"La restauración falló: {dbRestoreResult.Message}. La base de datos fue revertida exitosamente a su estado original previo."
                                };
                            }
                            else
                            {
                                _logger.LogError("El rollback automático reportó: {Message}", rollbackResult.Message);
                            }
                        }
                        catch (Exception rollEx)
                        {
                            _logger.LogError(rollEx, "Error crítico durante el intento de rollback");
                        }
                    }

                    return new RestoreResultDto
                    {
                        Success = false,
                        Message = dbRestoreResult.Message
                    };
                }

                // Restauración exitosa: limpiar snapshot de seguridad previo
                if (safetyBackupCreated && !string.IsNullOrEmpty(safetyBackupFile) && File.Exists(safetyBackupFile))
                {
                    try { File.Delete(safetyBackupFile); } catch { }
                }

                _logger.LogInformation("Base de datos restaurada exitosamente");
                _logger.LogInformation("=== RESTAURACIÓN COMPLETADA EXITOSAMENTE ===");
                _logger.LogInformation("IMPORTANTE: Debe reiniciar la aplicación para que los cambios surtan efecto");

                return new RestoreResultDto
                {
                    Success = true,
                    Message = "Restauración completada exitosamente. Por favor reinicie la aplicación.",
                    RestoredAt = DateTime.Now
                };
            }
            finally
            {
                // Limpiar directorio temporal (excepto archivos de config para revisión manual)
                if (Directory.Exists(tempDir) && !isZipFile)
                {
                    _logger.LogInformation("Limpiando directorio temporal...");
                    Directory.Delete(tempDir, true);
                    _logger.LogInformation("Directorio temporal eliminado");
                }
                else if (isZipFile)
                {
                    _logger.LogInformation("Directorio temporal conservado para revisión: {TempDir}", tempDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico durante la restauración del respaldo");
            return new RestoreResultDto
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public async Task<IEnumerable<BackupDto>> GetAvailableBackupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo lista de respaldos disponibles...");

            var backupDirectory = await GetBackupDirectoryAsync();
            _logger.LogInformation("Directorio de respaldos: {BackupDirectory}", backupDirectory);

            if (!Directory.Exists(backupDirectory))
            {
                _logger.LogWarning("El directorio de respaldos NO existe: {BackupDirectory}", backupDirectory);
                return Enumerable.Empty<BackupDto>();
            }

            var backupFiles = Directory.GetFiles(backupDirectory, "*.*")
                .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogInformation("Se encontraron {Count} archivos de respaldo", backupFiles.Count);

            var backups = new List<BackupDto>();

            foreach (var file in backupFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var backupType = file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "Full" : "DatabaseOnly";
                    var description = "";

                    // Intentar leer metadata si es un archivo zip
                    if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var archive = ZipFile.OpenRead(file);
                            var metadataEntry = archive.GetEntry("metadata.json");
                            if (metadataEntry != null)
                            {
                                using var stream = metadataEntry.Open();
                                using var reader = new StreamReader(stream);
                                var metadataJson = await reader.ReadToEndAsync(cancellationToken);
                                var metadata = JsonSerializer.Deserialize<BackupMetadata>(metadataJson);
                                description = metadata?.Description ?? "";
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error al leer metadata del archivo: {Title}", fileInfo.Name);
                        }
                    }

                    backups.Add(new BackupDto
                    {
                        FileName = fileInfo.Name,
                        FilePath = fileInfo.FullName,
                        SizeInBytes = fileInfo.Length,
                        CreatedAt = fileInfo.CreationTime,
                        BackupType = backupType,
                        Description = description
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al procesar archivo de respaldo: {File}", file);
                }
            }

            return backups.OrderByDescending(b => b.CreatedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener lista de respaldos");
            return Enumerable.Empty<BackupDto>();
        }
    }

    public Task<bool> DeleteBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(backupFilePath))
            {
                _logger.LogWarning("El archivo de respaldo no existe: {FilePath}", backupFilePath);
                return Task.FromResult(false);
            }

            File.Delete(backupFilePath);
            _logger.LogInformation("Respaldo eliminado: {FilePath}", backupFilePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar respaldo: {FilePath}", backupFilePath);
            return Task.FromResult(false);
        }
    }

    public Task<bool> ValidateBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(backupFilePath))
            {
                return Task.FromResult(false);
            }

            // Validar que el archivo no esté corrupto
            if (backupFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(backupFilePath);
                // Si puede abrir el archivo, es válido
                return Task.FromResult(archive.Entries.Count > 0);
            }
            else
            {
                // Para archivos .backup/.bak, solo verificar que existan y tengan contenido
                var fileInfo = new FileInfo(backupFilePath);
                return Task.FromResult(fileInfo.Length > 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar respaldo: {FilePath}", backupFilePath);
            return Task.FromResult(false);
        }
    }

    public async Task<BackupResultDto> UploadBackupAsync(string fileName, Stream contentStream, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== CARGANDO ARCHIVO DE RESPALDO ===");
        _logger.LogInformation("Nombre recibido: {FileName}", fileName);

        try
        {
            var cleanFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(cleanFileName))
            {
                return new BackupResultDto
                {
                    Success = false,
                    Message = "Nombre de archivo inválido."
                };
            }

            var extension = Path.GetExtension(cleanFileName).ToLowerInvariant();
            if (extension != ".zip" && extension != ".backup" && extension != ".bak")
            {
                return new BackupResultDto
                {
                    Success = false,
                    Message = $"Extensión '{extension}' no permitida. Solo se admiten archivos .zip, .backup o .bak."
                };
            }

            var backupDirectory = await GetBackupDirectoryAsync();
            var destinationPath = Path.Combine(backupDirectory, cleanFileName);

            _logger.LogInformation("Guardando respaldo en: {DestinationPath}", destinationPath);

            // Escribir archivo en disco
            await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await contentStream.CopyToAsync(fileStream, cancellationToken);
            }

            // Validar integridad del archivo recibido
            var isValid = await ValidateBackupAsync(destinationPath, cancellationToken);
            if (!isValid)
            {
                _logger.LogWarning("El archivo de respaldo subido no superó la validación de integridad: {DestinationPath}", destinationPath);
                if (File.Exists(destinationPath))
                {
                    try { File.Delete(destinationPath); } catch { }
                }

                return new BackupResultDto
                {
                    Success = false,
                    Message = "El archivo cargado está dañado o no es un formato de respaldo válido."
                };
            }

            var fileInfo = new FileInfo(destinationPath);
            _logger.LogInformation("Respaldo cargado exitosamente. Tamaño: {SizeBytes} bytes ({SizeMB:F2} MB)", fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);

            return new BackupResultDto
            {
                Success = true,
                Message = "Respaldo cargado exitosamente.",
                BackupFilePath = destinationPath,
                SizeInBytes = fileInfo.Length,
                CreatedAt = fileInfo.LastWriteTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar el respaldo cargado");
            return new BackupResultDto
            {
                Success = false,
                Message = $"Error al cargar respaldo: {ex.Message}"
            };
        }
    }

    public async Task<string?> GetBackupFilePathAsync(string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var cleanFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(cleanFileName))
            {
                return null;
            }

            var backupDirectory = await GetBackupDirectoryAsync();
            var targetPath = Path.Combine(backupDirectory, cleanFileName);

            // Prevenir Path Traversal
            var fullTargetPath = Path.GetFullPath(targetPath);
            var fullBackupDir = Path.GetFullPath(backupDirectory);

            if (!fullTargetPath.StartsWith(fullBackupDir, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Intento de acceso fuera del directorio de respaldos: {Target}", fullTargetPath);
                return null;
            }

            if (!File.Exists(fullTargetPath))
            {
                _logger.LogWarning("Archivo de respaldo solicitado no encontrado: {Target}", fullTargetPath);
                return null;
            }

            return fullTargetPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al resolver la ruta del archivo de respaldo: {FileName}", fileName);
            return null;
        }
    }

    #region Private Methods

    private (bool Valid, string Message) ValidateStoragePrerequisites(string directoryPath, long requiredBytes = 100 * 1024 * 1024)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // 1. Validar permisos de escritura creando y eliminando un archivo temporal
            var testFilePath = Path.Combine(directoryPath, $".perm_test_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(testFilePath, "test_perm");
                File.Delete(testFilePath);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, $"Sin permisos de escritura en el directorio destino: {directoryPath}");
            }
            catch (Exception ex)
            {
                return (false, $"Error al verificar permisos de escritura en {directoryPath}: {ex.Message}");
            }

            // 2. Validar espacio en disco disponible (si el volumen está disponible)
            var fullPath = Path.GetFullPath(directoryPath);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
                {
                    var freeMb = drive.AvailableFreeSpace / 1024.0 / 1024.0;
                    var reqMb = requiredBytes / 1024.0 / 1024.0;
                    return (false, $"Espacio insuficiente en disco ({root}). Disponible: {freeMb:F1} MB, Requerido mínimo: {reqMb:F1} MB.");
                }
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo validar el espacio de disco para {Path}", directoryPath);
            return (true, string.Empty);
        }
    }

    private string DiagnosePostgreSQLError(string rawError, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return $"Proceso finalizó con código de salida {exitCode}.";
        }

        var lower = rawError.ToLowerInvariant();
        if (lower.Contains("lock timeout") || lower.Contains("lock_timeout") || lower.Contains("canceling statement due to lock timeout"))
        {
            return "Operación cancelada por bloqueo en tabla: Otra transacción activa en PostgreSQL tiene tablas bloqueadas.";
        }
        if (lower.Contains("password authentication failed") || lower.Contains("no password was provided") || lower.Contains("fe_sendauth"))
        {
            return "Fallo de autenticación: Credenciales de PostgreSQL incorrectas o no reconocidas.";
        }
        if (lower.Contains("could not connect to server") || lower.Contains("connection refused") || lower.Contains("timeout expired") || lower.Contains("connection to server was lost"))
        {
            return "No se pudo conectar con el servidor PostgreSQL (timeout de conexión o servicio inactivo).";
        }
        if (lower.Contains("no space left on device") || lower.Contains("disk full") || lower.Contains("espacio insuficiente"))
        {
            return "Espacio en disco insuficiente durante la operación de base de datos.";
        }
        if (lower.Contains("permission denied") || lower.Contains("acceso denegado"))
        {
            return "Permiso denegado por el sistema de archivos o PostgreSQL.";
        }

        return rawError.Trim();
    }

    private async Task<(bool Success, string Message)> CreateDatabaseBackupFileAsync(string outputPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("--- Iniciando respaldo de base de datos con pg_dump ---");
        _logger.LogInformation("Archivo de salida: {OutputPath}", outputPath);

        try
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                var storageCheck = ValidateStoragePrerequisites(outputDir);
                if (!storageCheck.Valid)
                {
                    _logger.LogError("Validación de almacenamiento fallida: {Message}", storageCheck.Message);
                    return (false, storageCheck.Message);
                }
            }

            // Usar pg_dump para crear respaldo
            _logger.LogInformation("Buscando pg_dump...");
            var pgDumpPath = FindPgDumpPath();

            if (string.IsNullOrEmpty(pgDumpPath))
            {
                return (false, "pg_dump no encontrado. Asegúrese de que PostgreSQL esté instalado.");
            }

            _logger.LogInformation("pg_dump encontrado en: {PgDumpPath}", pgDumpPath);

            // -w: No interactivo (falla si pide pass)
            // --lock-wait-timeout=30000: Falla en 30s si hay un lock en vez de congelarse
            var arguments = $"-h {_postgresHost} -p {_postgresPort} -U \"{_postgresUser}\" -w --lock-wait-timeout=30000 -F c -b -v -f \"{outputPath}\" \"{_postgresDatabase}\"";
            _logger.LogInformation("Configuración de conexión: Host={Host}, Port={Port}, User={User}, DB={Database}",
                _postgresHost, _postgresPort, _postgresUser, _postgresDatabase);
            _logger.LogInformation("Comando: pg_dump {Arguments}", arguments.Replace(_postgresPassword, "***"));

            var startInfo = new ProcessStartInfo
            {
                FileName = pgDumpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(pgDumpPath)
            };

            startInfo.EnvironmentVariables["PGPASSWORD"] = _postgresPassword;
            startInfo.EnvironmentVariables["PGCONNECT_TIMEOUT"] = "10";

            _logger.LogInformation("Ejecutando pg_dump con detección de actividad (stall-watchdog)...");
            using var process = new Process { StartInfo = startInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var lastActivityTime = DateTime.UtcNow;
            var stallTimeout = TimeSpan.FromSeconds(90); // 90 segundos sin actividad indica proceso colgado

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lastActivityTime = DateTime.UtcNow;
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lastActivityTime = DateTime.UtcNow;
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();

            _logger.LogInformation("Proceso pg_dump iniciado con PID: {ProcessId}", process.Id);

            long previousFileSize = 0;
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch { }
                    return (false, "Operación cancelada por el usuario.");
                }

                if (File.Exists(outputPath))
                {
                    try
                    {
                        var currentLength = new FileInfo(outputPath).Length;
                        if (currentLength > previousFileSize)
                        {
                            previousFileSize = currentLength;
                            lastActivityTime = DateTime.UtcNow;
                        }
                    }
                    catch { }
                }

                if (DateTime.UtcNow - lastActivityTime > stallTimeout)
                {
                    _logger.LogError("pg_dump excedió el tiempo límite de inactividad de {Seconds} segundos", stallTimeout.TotalSeconds);
                    try { process.Kill(true); } catch { }
                    return (false, $"pg_dump se canceló por inactividad prolongada ({stallTimeout.TotalSeconds} segundos sin progreso).");
                }

                await Task.Delay(500, cancellationToken);
            }

            await Task.Delay(100, cancellationToken);

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            _logger.LogInformation("pg_dump finalizado con código de salida: {ExitCode}", process.ExitCode);

            if (!string.IsNullOrWhiteSpace(output))
            {
                _logger.LogInformation("Salida estándar de pg_dump:\n{Output}", output);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogInformation("Salida de error/progreso de pg_dump:\n{Error}", error);
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("pg_dump falló con código de salida {ExitCode}", process.ExitCode);
                var diagnosis = DiagnosePostgreSQLError(error, process.ExitCode);
                return (false, $"pg_dump falló: {diagnosis}");
            }

            if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("Archivo de respaldo creado exitosamente. Tamaño: {SizeBytes} bytes ({SizeMB:F2} MB)",
                    fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);
            }
            else
            {
                _logger.LogError("El archivo de respaldo no se creó: {OutputPath}", outputPath);
                return (false, "El archivo de respaldo no se generó correctamente.");
            }

            _logger.LogInformation("--- Respaldo de base de datos completado exitosamente ---");
            return (true, "Éxito");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar pg_dump");
            return (false, $"Excepción al ejecutar respaldo: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> RestoreDatabaseFromFileAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        try
        {
            var pgRestorePath = FindPgRestorePath();
            if (string.IsNullOrEmpty(pgRestorePath))
            {
                return (false, "pg_restore no encontrado. Asegúrese de que PostgreSQL esté instalado.");
            }

            if (!File.Exists(backupFilePath))
            {
                return (false, $"El archivo de respaldo a restaurar no existe: {backupFilePath}");
            }

            _logger.LogWarning("Iniciando restauración en base de datos {Database} con transacción atómica (--single-transaction)...", _postgresDatabase);

            // -w: no contraseña interactiva
            // --single-transaction: ejecuta todo en una sola transacción BEGIN ... COMMIT (si falla, hace rollback automático)
            // --clean --if-exists: elimina objetos antes de recrearlos
            var arguments = $"-h {_postgresHost} -p {_postgresPort} -U \"{_postgresUser}\" -d \"{_postgresDatabase}\" -w --single-transaction --clean --if-exists -v \"{backupFilePath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = pgRestorePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            startInfo.EnvironmentVariables["PGPASSWORD"] = _postgresPassword;
            startInfo.EnvironmentVariables["PGCONNECT_TIMEOUT"] = "10";

            _logger.LogInformation("Ejecutando pg_restore...");
            using var process = new Process { StartInfo = startInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var lastActivityTime = DateTime.UtcNow;
            var stallTimeout = TimeSpan.FromSeconds(120); // 120s de inactividad para restore

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lastActivityTime = DateTime.UtcNow;
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lastActivityTime = DateTime.UtcNow;
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();

            _logger.LogInformation("Proceso pg_restore iniciado con PID: {ProcessId}", process.Id);

            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch { }
                    return (false, "Operación cancelada por el usuario.");
                }

                if (DateTime.UtcNow - lastActivityTime > stallTimeout)
                {
                    _logger.LogError("pg_restore excedió el tiempo de inactividad de {Seconds} segundos", stallTimeout.TotalSeconds);
                    try { process.Kill(true); } catch { }
                    return (false, $"pg_restore se canceló por inactividad prolongada ({stallTimeout.TotalSeconds} segundos sin respuesta).");
                }

                await Task.Delay(500, cancellationToken);
            }

            await Task.Delay(100, cancellationToken);

            var error = errorBuilder.ToString();
            var output = outputBuilder.ToString();

            _logger.LogInformation("pg_restore código de salida: {ExitCode}", process.ExitCode);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("pg_restore finalizó con código {ExitCode}. Analizando errores...", process.ExitCode);

                if (error.Contains("fatal:", StringComparison.OrdinalIgnoreCase) || error.Contains("error:", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Se detectaron errores críticos en la restauración.");
                    var diagnosis = DiagnosePostgreSQLError(error, process.ExitCode);
                    return (false, $"pg_restore falló: {diagnosis}");
                }
            }

            return (true, "Éxito");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar pg_restore");
            return (false, $"Excepción al ejecutar restauración: {ex.Message}");
        }
    }

    private string? FindPgDumpPath()
    {
        // Buscar pg_dump en ubicaciones comunes
        var commonPaths = new[]
        {
            @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
            @"C:\Program Files\PostgreSQL\15\bin\pg_dump.exe",
            @"C:\Program Files\PostgreSQL\14\bin\pg_dump.exe",
            @"C:\Program Files\PostgreSQL\13\bin\pg_dump.exe",
            @"C:\Program Files (x86)\PostgreSQL\16\bin\pg_dump.exe",
            @"C:\Program Files (x86)\PostgreSQL\15\bin\pg_dump.exe",
            @"C:\Program Files (x86)\PostgreSQL\14\bin\pg_dump.exe",
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Intentar encontrar en PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            var paths = pathEnv.Split(';');
            foreach (var path in paths)
            {
                var pgDumpPath = Path.Combine(path, "pg_dump.exe");
                if (File.Exists(pgDumpPath))
                {
                    return pgDumpPath;
                }
            }
        }

        return null;
    }

    private string? FindPgRestorePath()
    {
        var pgDumpPath = FindPgDumpPath();
        if (pgDumpPath != null)
        {
            var dir = Path.GetDirectoryName(pgDumpPath);
            if (dir != null)
            {
                var pgRestorePath = Path.Combine(dir, "pg_restore.exe");
                if (File.Exists(pgRestorePath))
                {
                    return pgRestorePath;
                }
            }
        }

        return null;
    }

    private Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2)
            {
                result[keyValue[0].Trim()] = keyValue[1].Trim();
            }
        }

        return result;
    }

    #endregion

    private class BackupMetadata
    {
        public string BackupType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string DatabaseName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}
