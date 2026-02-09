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
    private readonly string _postgresHost;
    private readonly string _postgresPort;
    private readonly string _postgresDatabase;
    private readonly string _postgresUser;
    private readonly string _postgresPassword;

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
            ?? throw new InvalidOperationException("Connection string 'AttendanceDb' no encontrada");

        var connParams = ParseConnectionString(connectionString);
        _postgresHost = connParams["Host"];
        _postgresPort = connParams["Port"];
        _postgresDatabase = connParams["Database"];
        _postgresUser = connParams["Username"];
        _postgresPassword = connParams["Password"];
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
                var dbBackupSuccess = await CreateDatabaseBackupFileAsync(dbBackupFile, cancellationToken);

                if (!dbBackupSuccess)
                {
                    _logger.LogError("Fallo al crear respaldo de base de datos");
                    return new BackupResultDto
                    {
                        Success = false,
                        Message = "Error al crear respaldo de base de datos"
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
            var success = await CreateDatabaseBackupFileAsync(backupFilePath, cancellationToken);

            if (!success)
            {
                return new BackupResultDto
                {
                    Success = false,
                    Message = "Error al crear respaldo de base de datos"
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

            try
            {
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
                        
                        var dbRestoreSuccess = await RestoreDatabaseFromFileAsync(dbBackupFile, cancellationToken);
                        if (!dbRestoreSuccess)
                        {
                            _logger.LogError("Falló la restauración de la base de datos");
                            return new RestoreResultDto
                            {
                                Success = false,
                                Message = "Error al restaurar base de datos. Revise los logs para más detalles."
                            };
                        }
                        _logger.LogInformation("Base de datos restaurada exitosamente");
                    }
                    else
                    {
                        _logger.LogError("No se encontró el archivo database.backup en el respaldo");
                        return new RestoreResultDto
                        {
                            Success = false,
                            Message = "El respaldo no contiene un archivo de base de datos válido"
                        };
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
                    var dbRestoreSuccess = await RestoreDatabaseFromFileAsync(backupFilePath, cancellationToken);
                    if (!dbRestoreSuccess)
                    {
                        _logger.LogError("Falló la restauración de la base de datos");
                        return new RestoreResultDto
                        {
                            Success = false,
                            Message = "Error al restaurar base de datos. Revise los logs para más detalles."
                        };
                    }
                    _logger.LogInformation("Base de datos restaurada exitosamente");
                }

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
            if (backupFilePath.EndsWith(".zip"))
            {
                using var archive = ZipFile.OpenRead(backupFilePath);
                // Si puede abrir el archivo, es válido
                return Task.FromResult(archive.Entries.Count > 0);
            }
            else
            {
                // Para archivos .backup, solo verificar que existan y tengan contenido
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

    #region Private Methods

    private async Task<bool> CreateDatabaseBackupFileAsync(string outputPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("--- Iniciando respaldo de base de datos con pg_dump ---");
        _logger.LogInformation("Archivo de salida: {OutputPath}", outputPath);
        
        try
        {
            // Usar pg_dump para crear respaldo
            _logger.LogInformation("Buscando pg_dump...");
            var pgDumpPath = FindPgDumpPath();
            
            if (string.IsNullOrEmpty(pgDumpPath))
            {
                _logger.LogError("pg_dump no encontrado. Asegúrese de que PostgreSQL esté instalado.");
                _logger.LogError("Rutas buscadas: C:\\Program Files\\PostgreSQL\\[VERSION]\\bin\\ y PATH del sistema");
                return false;
            }
            
            _logger.LogInformation("pg_dump encontrado en: {PgDumpPath}", pgDumpPath);

            var arguments = $"-h {_postgresHost} -p {_postgresPort} -U {_postgresUser} -F c -b -v -f \"{outputPath}\" {_postgresDatabase}";
            _logger.LogInformation("Configuración de conexión:");
            _logger.LogInformation("  Host: {Host}", _postgresHost);
            _logger.LogInformation("  Puerto: {Port}", _postgresPort);
            _logger.LogInformation("  Usuario: {User}", _postgresUser);
            _logger.LogInformation("  Base de datos: {Database}", _postgresDatabase);
            _logger.LogInformation("Comando: pg_dump {Arguments}", arguments.Replace(_postgresPassword, "***"));
            
            var startInfo = new ProcessStartInfo
            {
                FileName = pgDumpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,  // Importante: evita que pg_dump espere entrada
                CreateNoWindow = true
            };

            // Configurar password via variable de entorno
            startInfo.EnvironmentVariables["PGPASSWORD"] = _postgresPassword;
            _logger.LogInformation("Password configurada en variable de entorno PGPASSWORD");

            _logger.LogInformation("Ejecutando pg_dump...");
            using var process = new Process { StartInfo = startInfo };
            
            // Capturar salida usando eventos para evitar deadlock
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };
            
            process.Start();
            
            // Iniciar lectura asíncrona
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Cerrar stdin inmediatamente para que pg_dump no espere entrada
            process.StandardInput.Close();
            
            _logger.LogInformation("Proceso pg_dump iniciado con PID: {ProcessId}", process.Id);

            // Obtener timeout configurado
            var config = await _systemConfigRepository.GetConfigurationAsync(cancellationToken);
            var timeoutMinutes = config?.BackupTimeoutMinutes ?? 10;
            _logger.LogInformation("Esperando finalización de pg_dump (timeout: {Timeout} minutos)...", timeoutMinutes);
            
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(timeoutMinutes), cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogError("pg_dump excedió el tiempo límite de {Timeout} minutos", timeoutMinutes);
                _logger.LogError("Salida parcial: {Output}", outputBuilder.ToString());
                _logger.LogError("Error parcial: {Error}", errorBuilder.ToString());
                try
                {
                    process.Kill(true);
                    _logger.LogWarning("Proceso pg_dump terminado forzosamente");
                }
                catch (Exception killEx)
                {
                    _logger.LogError(killEx, "Error al terminar proceso pg_dump");
                }
                return false;
            }

            // Esperar a que termine de leer toda la salida
            await Task.Delay(100, cancellationToken); // Pequeña espera para asegurar que se leyó todo
            
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            _logger.LogInformation("pg_dump finalizado con código de salida: {ExitCode}", process.ExitCode);

            if (!string.IsNullOrWhiteSpace(output))
            {
                _logger.LogInformation("Salida estándar de pg_dump:");
                _logger.LogInformation(output);
            }
            
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogInformation("Salida de error de pg_dump (puede contener mensajes informativos):");
                _logger.LogInformation(error);
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("pg_dump falló con código de salida {ExitCode}", process.ExitCode);
                _logger.LogError("Error: {Error}", error);
                return false;
            }
            
            // Verificar que el archivo se creó
            if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("Archivo de respaldo creado exitosamente");
                _logger.LogInformation("Tamaño: {SizeBytes} bytes ({SizeMB:F2} MB)", fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);
            }
            else
            {
                _logger.LogError("El archivo de respaldo no se creó: {OutputPath}", outputPath);
                return false;
            }

            _logger.LogInformation("--- Respaldo de base de datos completado exitosamente ---");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar pg_dump");
            return false;
        }
    }

    private async Task<bool> RestoreDatabaseFromFileAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        try
        {
            // Usar pg_restore para restaurar respaldo
            var pgRestorePath = FindPgRestorePath();
            if (string.IsNullOrEmpty(pgRestorePath))
            {
                _logger.LogError("pg_restore no encontrado. Asegúrese de que PostgreSQL esté instalado.");
                return false;
            }

            // Primero, limpiar la base de datos (drop y recrear)
            _logger.LogWarning("ADVERTENCIA: Se eliminará y recreará la base de datos {Database}", _postgresDatabase);

            var startInfo = new ProcessStartInfo
            {
                FileName = pgRestorePath,
                Arguments = $"-h {_postgresHost} -p {_postgresPort} -U {_postgresUser} -d {_postgresDatabase} -c -v \"{backupFilePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            startInfo.EnvironmentVariables["PGPASSWORD"] = _postgresPassword;

            _logger.LogInformation("Ejecutando pg_restore...");
            using var process = new Process { StartInfo = startInfo };
            
            // Capturar salida usando eventos para evitar deadlock
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Cerrar stdin
            process.StandardInput.Close();

            _logger.LogInformation("Proceso pg_restore iniciado con PID: {ProcessId}", process.Id);

            // Timeout (usar el mismo que backup o uno mayor, restaurar suele ser más lento)
            var config = await _systemConfigRepository.GetConfigurationAsync(cancellationToken);
            var timeoutMinutes = (config?.BackupTimeoutMinutes ?? 10) * 2; // Doble de tiempo para restaurar
            _logger.LogInformation("Esperando finalización de pg_restore (timeout: {Timeout} minutos)...", timeoutMinutes);

            var waitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(timeoutMinutes), cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("pg_restore excedió el tiempo límite de {Timeout} minutos", timeoutMinutes);
                try { process.Kill(true); } catch { }
                return false;
            }

            // Esperar lectura completa
            await Task.Delay(100, cancellationToken);

            var error = errorBuilder.ToString();
            var output = outputBuilder.ToString();
            
            _logger.LogInformation("pg_restore código de salida: {ExitCode}", process.ExitCode);

            // pg_restore puede retornar warnings (código 1) que no son errores fatales
            if (process.ExitCode != 0)
            {
                // Si hay error, loguearlo, pero a veces exit code 1 es warning.
                // Sin embargo, para seguridad, si no es 0 revisamos output
                _logger.LogWarning("pg_restore finalizó con código {ExitCode}. Revise los logs por posibles advertencias.", process.ExitCode);
                _logger.LogInformation("Salida de error: {Error}", error);
                
                // Considerar fallo si hay errores críticos en el log
                if (error.Contains("fatal:", StringComparison.OrdinalIgnoreCase) || error.Contains("error:", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Se detectaron errores críticos en la restauración.");
                    return false;
                }
            }

            return true;

            _logger.LogInformation("Base de datos restaurada exitosamente");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar pg_restore");
            return false;
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
        var result = new Dictionary<string, string>();
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
