using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.Infrastructure.Services;

public class BackupService : IBackupService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupService> _logger;
    private readonly ISystemConfigurationRepository _systemConfigRepository;
    private readonly IRestoreStateService _restoreStateService;
    private readonly IBackgroundJobControlService _backgroundJobControlService;
    private readonly TimeSpan _dumpStallTimeout;
    private readonly TimeSpan _restoreStallTimeout;
    private readonly TimeSpan _drainTimeout;
    private readonly string _connectionString;
    private readonly string _postgresHost = string.Empty;
    private readonly string _postgresPort = string.Empty;
    private readonly string _postgresDatabase = string.Empty;
    private readonly string _postgresUser = string.Empty;
    private readonly string _postgresPassword = string.Empty;

    public BackupService(
        IConfiguration configuration,
        ILogger<BackupService> logger,
        ISystemConfigurationRepository systemConfigRepository,
        IRestoreStateService? restoreStateService = null,
        IBackgroundJobControlService? backgroundJobControlService = null)
    {
        _configuration = configuration;
        _logger = logger;
        _systemConfigRepository = systemConfigRepository;
        _restoreStateService = restoreStateService ?? new RestoreStateService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<RestoreStateService>());
        _backgroundJobControlService = backgroundJobControlService ?? new NullBackgroundJobControlService();

        var dumpTimeoutSec = configuration.GetValue<int>("Backup:DumpStallTimeoutSeconds", 600);
        var restoreTimeoutSec = configuration.GetValue<int>("Backup:RestoreStallTimeoutSeconds", 900);
        _dumpStallTimeout = TimeSpan.FromSeconds(Math.Max(dumpTimeoutSec, 60));
        _restoreStallTimeout = TimeSpan.FromSeconds(Math.Max(restoreTimeoutSec, 120));
        _drainTimeout = TimeSpan.FromSeconds(Math.Max(configuration.GetValue<int>("Backup:DrainTimeoutSeconds", 60), 1));

        // Parsear connection string de PostgreSQL de forma robusta con NpgsqlConnectionStringBuilder
        var connectionString = configuration.GetConnectionString("AttendanceDb")
            ?? throw new InvalidOperationException("Connection string 'AttendanceDb' no encontrada en la configuración.");

        _connectionString = connectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        _postgresHost = builder.Host ?? "localhost";
        _postgresPort = builder.Port.ToString();
        _postgresDatabase = builder.Database ?? "";
        _postgresUser = builder.Username ?? "";
        _postgresPassword = builder.Password ?? "";

        if (string.IsNullOrEmpty(_postgresHost) || string.IsNullOrEmpty(_postgresDatabase) || string.IsNullOrEmpty(_postgresUser))
        {
            _logger.LogError("Faltan parámetros críticos en la cadena de conexión para el respaldo: Host={Host}, DB={DB}, User={User}",
                _postgresHost, _postgresDatabase, _postgresUser);
        }
    }

    public DatabaseConnectionInfoDto GetDatabaseConnectionInfo()
    {
        return new DatabaseConnectionInfoDto
        {
            Host = _postgresHost,
            Port = int.TryParse(_postgresPort, out var p) ? p : 5432,
            Database = _postgresDatabase,
            Username = _postgresUser,
            Engine = "PostgreSQL"
        };
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

                // Purgar respaldos antiguos según la política de retención
                try
                {
                    await PruneOldBackupsAsync(cancellationToken: cancellationToken);
                }
                catch (Exception pruneEx)
                {
                    _logger.LogWarning(pruneEx, "Aviso: No se pudo completar la purga de respaldos antiguos tras crear el respaldo.");
                }

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

            // Purgar respaldos antiguos según la política de retención
            try
            {
                await PruneOldBackupsAsync(cancellationToken: cancellationToken);
            }
            catch (Exception pruneEx)
            {
                _logger.LogWarning(pruneEx, "Aviso: No se pudo completar la purga de respaldos antiguos tras crear el respaldo.");
            }

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

    public async Task<RestoreResultDto> RestoreBackupAsync(string backupFilePath, IProgress<RestoreProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== INICIANDO RESTAURACIÓN DE RESPALDO ===");
        _logger.LogInformation("Archivo de respaldo: {BackupFilePath}", backupFilePath);

        var ownerId = $"Restore_{Guid.NewGuid():N}";
        var lockAcquired = false;
        var jobsPaused = false;
        // Sólo pasa a true cuando un COMMIT de la restauración pudo haberse aplicado: antes de eso la base está intacta.
        var databaseMayHaveChanged = false;
        string? safetyBackupFile = null;
        var tempDir = Path.Combine(Path.GetTempPath(), $"AttendanceRestore_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        var isZipFile = backupFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(backupFilePath))
            {
                _logger.LogError("El archivo de respaldo no existe: {BackupFilePath}", backupFilePath);
                progress?.Report(new RestoreProgressReport(RestoreStage.Failed, "Error", "El archivo de respaldo no existe", IsError: true));
                return new RestoreResultDto
                {
                    Success = false,
                    Message = "El archivo de respaldo no existe"
                };
            }

            var fileInfo = new FileInfo(backupFilePath);
            _logger.LogInformation("Tamaño del archivo: {SizeBytes} bytes ({SizeMB:F2} MB)", fileInfo.Length, fileInfo.Length / 1024.0 / 1024.0);

            // 1. Adquirir bloqueo exclusivo de mantenimiento (H05 - Rechazo de restauraciones simultáneas)
            lockAcquired = await _restoreStateService.TryAcquireRestoreLockAsync(ownerId, $"Restauración desde {Path.GetFileName(backupFilePath)}", cancellationToken);
            if (!lockAcquired)
            {
                var msg = $"Operación rechazada: Ya existe otra restauración o mantenimiento en curso ({_restoreStateService.CurrentState} por {_restoreStateService.CurrentOwner ?? "desconocido"}).";
                _logger.LogError(msg);
                progress?.Report(new RestoreProgressReport(RestoreStage.Failed, "Restauración Rechazada", msg, IsError: true));
                return new RestoreResultDto { Success = false, Message = msg };
            }

            Directory.CreateDirectory(tempDir);
            _logger.LogInformation("Directorio temporal creado: {TempDir}", tempDir);

            // Determinar archivo .backup a validar y restaurar
            string targetDbFile;
            if (isZipFile)
            {
                _logger.LogInformation("[1/3] Extrayendo paquete completo para inspección...");
                progress?.Report(new RestoreProgressReport(RestoreStage.PreparingDatabase, "Preparación", "Extrayendo archivo comprimido para validación...", ProgressPercentage: 10));
                ZipFile.ExtractToDirectory(backupFilePath, tempDir);

                targetDbFile = Path.Combine(tempDir, "database.backup");
                if (!File.Exists(targetDbFile))
                {
                    return await AbortRestoreUnchangedAsync(ownerId, jobsPaused, null,
                        "El paquete de respaldo no contiene el archivo database.backup esperado.", progress);
                }
            }
            else
            {
                targetDbFile = backupFilePath;
            }

            // 2. Pre-validación del archivo de respaldo con pg_restore -l (H04)
            // Si el archivo está corrupto o es inválido, abortamos inmediatamente SIN tocar esquemas ni conexiones
            progress?.Report(new RestoreProgressReport(RestoreStage.PreparingDatabase, "Validación Previa", "Verificando integridad y estructura del archivo de respaldo...", ProgressPercentage: 15));
            var archiveValidation = await ValidateBackupArchiveAsync(targetDbFile, cancellationToken);
            if (!archiveValidation.Valid)
            {
                return await AbortRestoreUnchangedAsync(ownerId, jobsPaused, null,
                    $"Cancelando restauración: {archiveValidation.Message}", progress);
            }

            // 3. Drenar escritores y pausar tareas ANTES del snapshot (H05): así el snapshot incluye todo lo
            // confirmado y un rollback no pierde escrituras que estaban en vuelo al adquirir el bloqueo.
            await _restoreStateService.TransitionStateAsync(MaintenanceState.Draining, "Drenando operaciones en vuelo", cancellationToken);
            progress?.Report(new RestoreProgressReport(RestoreStage.PreparingDatabase, "Drenaje", "Esperando a que terminen las operaciones en curso...", ProgressPercentage: 20));
            await _restoreStateService.DrainActiveWritersAsync(_drainTimeout, cancellationToken);

            var pendingWriters = _restoreStateService.ActiveWritersCount;
            if (pendingWriters > 0)
            {
                return await AbortRestoreUnchangedAsync(ownerId, jobsPaused, null,
                    $"Cancelando restauración: {pendingWriters} operación(es) de escritura siguen en curso después de {(int)_drainTimeout.TotalSeconds} segundos. Intente de nuevo cuando terminen.",
                    progress);
            }

            try
            {
                await _backgroundJobControlService.PauseBackgroundJobsAsync(cancellationToken);
                jobsPaused = true;
            }
            catch (Exception bgEx)
            {
                _logger.LogWarning(bgEx, "Aviso: No se pudieron pausar los trabajos en segundo plano antes de la restauración.");
            }

            // 4. Crear Snapshot de Seguridad previo antes de tocar la base de datos (H04)
            var safetyBackupCreated = false;
            try
            {
                progress?.Report(new RestoreProgressReport(RestoreStage.SafetySnapshot, "Snapshot de Seguridad", "Generando snapshot de seguridad previo para rollback automático...", ProgressPercentage: 25));
                var backupDirectory = await GetBackupDirectoryAsync();
                safetyBackupFile = Path.Combine(backupDirectory, $".safety_snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.backup");
                _logger.LogInformation("Generando snapshot de seguridad previo en: {SafetyBackupFile}", safetyBackupFile);
                var safetyResult = await CreateDatabaseBackupFileAsync(safetyBackupFile, cancellationToken);
                if (safetyResult.Success)
                {
                    var snapshotValidation = await ValidateBackupArchiveAsync(safetyBackupFile, cancellationToken);
                    if (snapshotValidation.Valid)
                    {
                        safetyBackupCreated = true;
                        _logger.LogInformation("Snapshot de seguridad previo creado y verificado exitosamente.");
                        progress?.Report(new RestoreProgressReport(RestoreStage.SafetySnapshot, "Snapshot de Seguridad", "Snapshot de seguridad previo generado y verificado.", ProgressPercentage: 30));
                    }
                    else
                    {
                        _logger.LogError("El snapshot de seguridad generado no superó la verificación de integridad: {Message}", snapshotValidation.Message);
                    }
                }
                else
                {
                    _logger.LogError("Fallo al generar el snapshot de seguridad previo: {Message}.", safetyResult.Message);
                }
            }
            catch (Exception snapEx)
            {
                _logger.LogError(snapEx, "Excepción al intentar crear snapshot de seguridad previo");
            }

            if (!safetyBackupCreated)
            {
                return await AbortRestoreUnchangedAsync(ownerId, jobsPaused, safetyBackupFile,
                    "Cancelando restauración: No se pudo generar o validar el snapshot de seguridad previo. Por contención de seguridad (H04), no se permite continuar sin una copia de respaldo recuperable verificada.",
                    progress);
            }

            // 5. Ejecutar la restauración física en una sola transacción
            await _restoreStateService.TransitionStateAsync(MaintenanceState.Restoring, "Restaurando base de datos en una sola transacción", cancellationToken);
            progress?.Report(new RestoreProgressReport(RestoreStage.ExecutingPgRestore, "Restauración", "Ejecutando pg_restore en base de datos...", ProgressPercentage: 50));

            var restoreAttempt = await RestoreDatabaseAtomicallyAsync(targetDbFile, tempDir, progress, cancellationToken);
            databaseMayHaveChanged = restoreAttempt.DatabaseMayHaveChanged;

            if (!restoreAttempt.Success)
            {
                _logger.LogError("Falló la restauración de la base de datos: {Message}", restoreAttempt.Message);

                if (!restoreAttempt.DatabaseMayHaveChanged)
                {
                    // No se envió COMMIT (o no se aplicó): PostgreSQL descartó la transacción completa.
                    return await AbortRestoreUnchangedAsync(ownerId, jobsPaused, safetyBackupFile,
                        $"La restauración falló: {restoreAttempt.Message}. La base de datos no se modificó.",
                        progress);
                }

                // Se envió COMMIT pero no se sabe si se aplicó: volver al snapshot.
                return await RollbackFromSnapshotAsync(ownerId, safetyBackupFile!, tempDir,
                    $"La restauración falló ({restoreAttempt.Message}) sin poder confirmar si se aplicó", progress);
            }

            // 6. Validación post-restauración (H04/H05)
            await _restoreStateService.TransitionStateAsync(MaintenanceState.Validating, "Validando esquema e integridad post-restauración", cancellationToken);
            progress?.Report(new RestoreProgressReport(RestoreStage.Finalizing, "Validación Post-Restauración", "Verificando integridad de tablas esenciales...", ProgressPercentage: 85));

            var postValidation = await ValidateDatabasePostRestoreAsync(cancellationToken);
            if (!postValidation.Valid)
            {
                _logger.LogError("La validación post-restauración falló: {Message}. Iniciando rollback...", postValidation.Message);
                return await RollbackFromSnapshotAsync(ownerId, safetyBackupFile!, tempDir,
                    $"Restauración rechazada por validación de datos fallida: {postValidation.Message}", progress);
            }

            // 7. Finalización exitosa
            progress?.Report(new RestoreProgressReport(RestoreStage.Finalizing, "Finalización", "Limpiando snapshot temporal...", ProgressPercentage: 95));
            TryDeleteFile(safetyBackupFile);

            NpgsqlConnection.ClearAllPools();
            await _restoreStateService.ReleaseRestoreLockAsync(ownerId, success: true, CancellationToken.None);
            await TryResumeBackgroundJobsAsync();

            _logger.LogInformation("Base de datos restaurada y verificada exitosamente.");
            progress?.Report(new RestoreProgressReport(RestoreStage.Completed, "Restauración Completada", "Base de datos restaurada exitosamente. Por favor reinicie la aplicación.", ProgressPercentage: 100));

            return new RestoreResultDto
            {
                Success = true,
                Message = "Restauración completada y verificada exitosamente. Por favor reinicie la aplicación.",
                RestoredAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico durante la restauración del respaldo");
            if (lockAcquired)
            {
                // Si la base no pudo cambiar, no hay motivo para dejar el sistema bloqueado en Failed.
                await _restoreStateService.ReleaseRestoreLockAsync(ownerId, success: !databaseMayHaveChanged, CancellationToken.None);
                if (!databaseMayHaveChanged && jobsPaused)
                {
                    await TryResumeBackgroundJobsAsync();
                }
            }
            return new RestoreResultDto
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
        finally
        {
            // El directorio temporal contiene el respaldo extraído (incluida la configuración): se borra siempre.
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch (Exception ex) { _logger.LogWarning(ex, "No se pudo eliminar el directorio temporal {TempDir}", tempDir); }
            }
        }
    }

    /// <summary>Termina una restauración que no llegó a modificar la base: libera el bloqueo y reanuda las tareas.</summary>
    private async Task<RestoreResultDto> AbortRestoreUnchangedAsync(
        string ownerId,
        bool jobsPaused,
        string? safetyBackupFile,
        string message,
        IProgress<RestoreProgressReport>? progress)
    {
        _logger.LogError(message);
        progress?.Report(new RestoreProgressReport(RestoreStage.Failed, "Restauración Cancelada", message, IsError: true));

        // El snapshot es idéntico a la base actual: no aporta nada conservarlo.
        TryDeleteFile(safetyBackupFile);

        NpgsqlConnection.ClearAllPools();
        await _restoreStateService.ReleaseRestoreLockAsync(ownerId, success: true, CancellationToken.None);
        if (jobsPaused)
        {
            await TryResumeBackgroundJobsAsync();
        }

        return new RestoreResultDto { Success = false, Message = message };
    }

    /// <summary>
    /// Vuelve al snapshot de seguridad. Si tampoco se puede, el sistema queda en Failed (persistido) con las tareas
    /// pausadas y se conserva el snapshot para recuperarlo a mano.
    /// </summary>
    private async Task<RestoreResultDto> RollbackFromSnapshotAsync(
        string ownerId,
        string safetyBackupFile,
        string tempDir,
        string reason,
        IProgress<RestoreProgressReport>? progress)
    {
        _logger.LogWarning("Iniciando reversión automática (Rollback) desde el snapshot de seguridad: {Reason}", reason);
        progress?.Report(new RestoreProgressReport(RestoreStage.Failed, "Rollback Automático", "Iniciando reversión automática al estado previo usando el snapshot de seguridad...", IsError: true));
        await _restoreStateService.TransitionStateAsync(MaintenanceState.Recovering, "Ejecutando rollback automático", CancellationToken.None);

        RestoreAttempt rollback;
        try
        {
            // Token independiente (H04): el usuario no puede cancelar el rollback; el watchdog de inactividad sí lo corta.
            rollback = await RestoreDatabaseAtomicallyAsync(safetyBackupFile, tempDir, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "🚨 Error crítico durante el intento de rollback");
            rollback = new RestoreAttempt(false, true, ex.Message);
        }

        NpgsqlConnection.ClearAllPools();

        if (rollback.Success)
        {
            _logger.LogInformation("Rollback completado con éxito. La base de datos se mantiene en su estado previo original.");
            progress?.Report(new RestoreProgressReport(RestoreStage.Failed, "Rollback Completado", "La base de datos fue revertida exitosamente a su estado original previo.", IsError: true));
            TryDeleteFile(safetyBackupFile);
            await _restoreStateService.ReleaseRestoreLockAsync(ownerId, success: true, CancellationToken.None);
            await TryResumeBackgroundJobsAsync();

            return new RestoreResultDto
            {
                Success = false,
                Message = $"{reason}. La base de datos fue revertida exitosamente a su estado original previo."
            };
        }

        _logger.LogCritical("🚨 El rollback automático falló: {Message}. Snapshot conservado en {SafetyBackupFile}", rollback.Message, safetyBackupFile);
        progress?.Report(new RestoreProgressReport(RestoreStage.Failed, "Rollback Fallido", $"El rollback automático falló: {rollback.Message}", IsError: true));
        await _restoreStateService.ReleaseRestoreLockAsync(ownerId, success: false, CancellationToken.None);

        return new RestoreResultDto
        {
            Success = false,
            Message = $"FALLO CRÍTICO: {reason} y el rollback automático también falló ({rollback.Message}). " +
                      $"El sistema permanece en mantenimiento, incluso tras reiniciar, hasta que una restauración termine bien. " +
                      $"Snapshot previo conservado en: {safetyBackupFile}"
        };
    }

    private async Task TryResumeBackgroundJobsAsync()
    {
        try
        {
            await _backgroundJobControlService.ResumeBackgroundJobsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Aviso: No se pudieron reanudar los trabajos en segundo plano tras la restauración.");
        }
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try { File.Delete(path); } catch (Exception ex) { _logger.LogWarning(ex, "No se pudo eliminar el archivo {Path}", path); }
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

    public async Task<int> PruneOldBackupsAsync(int? keepCount = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var retention = keepCount ?? _configuration.GetValue<int>("Backup:KeepLastNBackups", 10);
            if (retention <= 0)
            {
                return 0;
            }

            var backupDir = await GetBackupDirectoryAsync();
            if (!Directory.Exists(backupDir))
            {
                return 0;
            }

            var backups = (await GetAvailableBackupsAsync(cancellationToken)).ToList();
            if (backups.Count <= retention)
            {
                return 0;
            }

            var toDelete = backups.OrderByDescending(b => b.CreatedAt).Skip(retention).ToList();
            var deletedCount = 0;

            foreach (var backup in toDelete)
            {
                _logger.LogInformation("Eliminando respaldo antiguo por política de retención (manteniendo los {Retention} más recientes): {Path}",
                    retention, backup.FilePath);

                if (File.Exists(backup.FilePath))
                {
                    try
                    {
                        File.Delete(backup.FilePath);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo eliminar el archivo de respaldo antiguo: {Path}", backup.FilePath);
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Purga completada: {Count} respaldos antiguos eliminados", deletedCount);
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al purgar los respaldos antiguos");
            return 0;
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
        if (lower.Contains("depende de") || lower.Contains("depends on") || lower.Contains("drop ... cascade"))
        {
            return "Conflicto de dependencias en la base de datos: Existen restricciones o tablas con claves foráneas dependientes. Se requiere eliminación en cascada previa.";
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
            // -T "logs" -T "Logs": Excluye tablas de logging para mantener respaldos ligeros y evitar tiempos excesivos
            // -N system_maintenance: el estado de mantenimiento es del servidor, no de los datos; no viaja en los respaldos
            var arguments = $"-h {_postgresHost} -p {_postgresPort} -U \"{_postgresUser}\" -w --lock-wait-timeout=30000 -T \"logs\" -T \"Logs\" -N \"{MaintenanceSchema}\" -F c -b -v -f \"{outputPath}\" \"{_postgresDatabase}\"";
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
            var lastHeartbeatLog = DateTime.UtcNow;
            var stallTimeout = _dumpStallTimeout;

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

            _logger.LogInformation("Proceso pg_dump iniciado con PID: {ProcessId} (Timeout inactividad: {TimeoutSeconds}s)", process.Id, (int)stallTimeout.TotalSeconds);

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

                if (DateTime.UtcNow - lastHeartbeatLog >= TimeSpan.FromSeconds(30))
                {
                    lastHeartbeatLog = DateTime.UtcNow;
                    try
                    {
                        var elapsed = DateTime.UtcNow - process.StartTime.ToUniversalTime();
                        var inactive = DateTime.UtcNow - lastActivityTime;
                        _logger.LogInformation("pg_dump sigue en ejecución (PID: {ProcessId}). Tiempo transcurrido: {Elapsed:mm\\:ss}. Última actividad hace {Inactive:mm\\:ss} (Límite: {TimeoutSeconds}s)",
                            process.Id, elapsed, inactive, (int)stallTimeout.TotalSeconds);
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

    private sealed record RestoreAttempt(bool Success, bool DatabaseMayHaveChanged, string Message);

    /// <summary>Esquema propio del estado de mantenimiento: ni se respalda ni se restaura, para que una restauración no lo pise.</summary>
    private const string MaintenanceSchema = "system_maintenance";

    /// <summary>Marca que psql imprime sólo si el COMMIT se aplicó (con ON_ERROR_STOP, un COMMIT fallido termina psql antes).</summary>
    private const string CommitMarker = "attendance_restore_committed";

    // Se ejecuta dentro de la misma transacción que el contenido del respaldo: si algo falla, también se deshace el borrado.
    private const string RestorePreambleSql = @"
SET lock_timeout = '30s';
BEGIN;
DROP SCHEMA IF EXISTS public CASCADE;
DROP SCHEMA IF EXISTS hangfire CASCADE;
CREATE SCHEMA public;
GRANT ALL ON SCHEMA public TO CURRENT_USER;
GRANT ALL ON SCHEMA public TO public;
";

    /// <summary>
    /// Restaura el archivo en una sola transacción: pg_restore genera el script y psql lo ejecuta tras el preámbulo que
    /// borra los esquemas. El COMMIT sólo se envía si pg_restore generó el script completo, así que cualquier fallo
    /// anterior (archivo dañado, error SQL, proceso cancelado o colgado) deja la base intacta.
    /// </summary>
    private async Task<RestoreAttempt> RestoreDatabaseAtomicallyAsync(
        string backupFilePath,
        string workDir,
        IProgress<RestoreProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var pgRestorePath = FindPgRestorePath();
        if (string.IsNullOrEmpty(pgRestorePath))
        {
            return new RestoreAttempt(false, false, "pg_restore no encontrado. Asegúrese de que PostgreSQL esté instalado.");
        }

        var psqlPath = FindPsqlPath(pgRestorePath);
        if (string.IsNullOrEmpty(psqlPath))
        {
            return new RestoreAttempt(false, false, "psql no encontrado. Asegúrese de que las herramientas cliente de PostgreSQL estén instaladas.");
        }

        if (!File.Exists(backupFilePath))
        {
            return new RestoreAttempt(false, false, $"El archivo de respaldo a restaurar no existe: {backupFilePath}");
        }

        var toc = await RunPgRestoreListAsync(pgRestorePath, backupFilePath, cancellationToken);
        if (toc.ExitCode != 0)
        {
            return new RestoreAttempt(false, false, $"Archivo de respaldo corrupto o en formato no compatible: {toc.Error.Trim()}");
        }

        Directory.CreateDirectory(workDir);
        var listFile = Path.Combine(workDir, $"restore_{Guid.NewGuid():N}.list");
        await File.WriteAllLinesAsync(listFile, ExcludeMaintenanceSchema(toc.Output), cancellationToken);

        _logger.LogInformation("Terminando conexiones activas a {Database} antes de restaurar...", _postgresDatabase);
        progress?.Report(new RestoreProgressReport(RestoreStage.PreparingDatabase, "Preparación de Base de Datos", "Terminando conexiones activas en PostgreSQL...", ProgressPercentage: 40));
        await TerminateOtherConnectionsAsync(cancellationToken);

        var applicationName = $"attendance_restore_{Guid.NewGuid():N}";
        var lastActivityTicks = DateTime.UtcNow.Ticks;
        void Touch() => Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);
        DateTime LastActivity() => new(Interlocked.Read(ref lastActivityTicks), DateTimeKind.Utc);

        var psqlErrors = new StringBuilder();
        var pgRestoreErrors = new StringBuilder();
        var commitSent = false;
        var committed = false;
        Process? psql = null;
        Process? pgRestore = null;

        _logger.LogWarning("Iniciando restauración de {Database} en una sola transacción (pg_restore | psql)...", _postgresDatabase);
        progress?.Report(new RestoreProgressReport(RestoreStage.ExecutingPgRestore, "Ejecución pg_restore", "Iniciando restauración en una sola transacción...", ProgressPercentage: 45));

        try
        {
            psql = CreatePgProcess(psqlPath,
                $"-X -q -v ON_ERROR_STOP=1 -h {_postgresHost} -p {_postgresPort} -U \"{_postgresUser}\" -d \"{_postgresDatabase}\" -w",
                applicationName);
            psql.StartInfo.RedirectStandardInput = true;
            psql.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                Touch();
                if (e.Data.Trim() == CommitMarker)
                {
                    committed = true;
                    return;
                }
                _logger.LogInformation("[psql] {Line}", e.Data);
            };
            psql.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                Touch();
                lock (psqlErrors) psqlErrors.AppendLine(e.Data);
                _logger.LogInformation("[psql] {Line}", e.Data);
                var isErr = e.Data.Contains("ERROR", StringComparison.Ordinal) || e.Data.Contains("FATAL", StringComparison.Ordinal);
                progress?.Report(new RestoreProgressReport(RestoreStage.ExecutingPgRestore, "Ejecución pg_restore", e.Data, IsError: isErr, ProgressPercentage: 65));
            };

            psql.Start();
            psql.BeginOutputReadLine();
            psql.BeginErrorReadLine();
            var psqlInput = psql.StandardInput.BaseStream;
            await psqlInput.WriteAsync(Encoding.UTF8.GetBytes(RestorePreambleSql), cancellationToken);

            // -f - : genera el script SQL por stdout sin conectarse; --clean --if-exists elimina cada objeto antes de recrearlo.
            pgRestore = CreatePgProcess(pgRestorePath, $"-f - --clean --if-exists -v -L \"{listFile}\" \"{backupFilePath}\"", applicationName: null);
            pgRestore.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                Touch();
                lock (pgRestoreErrors) pgRestoreErrors.AppendLine(e.Data);
                _logger.LogInformation("[pg_restore] {Line}", e.Data);
                var isErr = e.Data.Contains("fatal:", StringComparison.OrdinalIgnoreCase) || e.Data.Contains("error:", StringComparison.OrdinalIgnoreCase);
                progress?.Report(new RestoreProgressReport(RestoreStage.ExecutingPgRestore, "Ejecución pg_restore", e.Data, IsError: isErr, ProgressPercentage: 65));
            };
            pgRestore.Start();
            pgRestore.BeginErrorReadLine();

            _logger.LogInformation("pg_restore (PID {RestorePid}) → psql (PID {PsqlPid}). Timeout inactividad: {TimeoutSeconds}s",
                pgRestore.Id, psql.Id, (int)_restoreStallTimeout.TotalSeconds);

            // Fase 1: pasar el script de pg_restore a psql.
            var copyTask = PipeAsync(pgRestore.StandardOutput.BaseStream, psqlInput, Touch);
            var stall = await WaitWithWatchdogAsync(copyTask, "pg_restore", applicationName, LastActivity, Touch, cancellationToken);
            if (stall != null)
            {
                KillProcess(pgRestore);
                KillProcess(psql);
                return new RestoreAttempt(false, false, stall);
            }

            Exception? pipeError = copyTask.Exception?.GetBaseException();
            if (pipeError != null)
            {
                // psql dejó de leer (normalmente terminó por un error SQL): pg_restore ya no tiene a quién escribir.
                KillProcess(pgRestore);
            }
            else
            {
                var pgRestoreExit = await WaitWithWatchdogAsync(pgRestore.WaitForExitAsync(), "pg_restore", applicationName, LastActivity, Touch, cancellationToken);
                if (pgRestoreExit != null)
                {
                    KillProcess(pgRestore);
                    KillProcess(psql);
                    return new RestoreAttempt(false, false, pgRestoreExit);
                }
            }

            if (pipeError == null && pgRestore.ExitCode == 0)
            {
                // Fase 2: confirmar. Sin este COMMIT, al cerrar la entrada psql se desconecta y PostgreSQL descarta todo.
                try
                {
                    await psqlInput.WriteAsync(Encoding.UTF8.GetBytes($"\nCOMMIT;\n\\echo {CommitMarker}\n"), CancellationToken.None);
                    await psqlInput.FlushAsync(CancellationToken.None);
                    commitSent = true;
                }
                catch (IOException ex)
                {
                    pipeError = ex;
                }
            }

            try { psql.StandardInput.Close(); } catch (IOException) { }

            var psqlExit = await WaitWithWatchdogAsync(psql.WaitForExitAsync(), "psql", applicationName, LastActivity, Touch,
                commitSent ? CancellationToken.None : cancellationToken);
            if (psqlExit != null)
            {
                // Si ya se envió el COMMIT no se sabe si se aplicó antes de cortar psql.
                KillProcess(psql);
                return new RestoreAttempt(false, commitSent, psqlExit);
            }

            // Esperar a que se vacíen los manejadores asíncronos de salida (el marcador de COMMIT llega por stdout).
            psql.WaitForExit();

            if (committed && psql.ExitCode == 0)
            {
                _logger.LogInformation("Restauración confirmada (COMMIT aplicado).");
                return new RestoreAttempt(true, true, "Éxito");
            }

            if (pipeError == null && pgRestore.ExitCode != 0)
            {
                var diagnosis = DiagnosePostgreSQLError(Snapshot(pgRestoreErrors), pgRestore.ExitCode);
                return new RestoreAttempt(false, false, $"pg_restore falló: {diagnosis}");
            }

            var psqlDiagnosis = DiagnosePostgreSQLError(Snapshot(psqlErrors), psql.ExitCode);
            if (pipeError != null && string.IsNullOrWhiteSpace(Snapshot(psqlErrors)))
            {
                psqlDiagnosis = pipeError.Message;
            }

            // Sin el marcador, psql terminó por sí solo antes de aplicar el COMMIT o éste falló: la transacción se descartó.
            return new RestoreAttempt(false, committed, $"psql falló: {psqlDiagnosis}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar la restauración");
            KillProcess(pgRestore);
            // Si psql terminó solo (p. ej. sin conexión), su stderr explica mejor el fallo que la tubería rota.
            try { psql?.WaitForExit(2000); } catch { }
            KillProcess(psql);
            var detail = Snapshot(psqlErrors);
            var message = string.IsNullOrWhiteSpace(detail) ? ex.Message : DiagnosePostgreSQLError(detail, -1);
            return new RestoreAttempt(false, committed || commitSent, $"Excepción al ejecutar restauración: {message}");
        }
        finally
        {
            KillProcess(pgRestore);
            KillProcess(psql);
            pgRestore?.Dispose();
            psql?.Dispose();
            try { File.Delete(listFile); } catch { }
        }
    }

    private static string Snapshot(StringBuilder builder)
    {
        lock (builder) return builder.ToString();
    }

    private static async Task PipeAsync(Stream source, Stream destination, Action onActivity)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            onActivity();
        }
        await destination.FlushAsync();
    }

    /// <summary>
    /// Espera a que termine <paramref name="work"/>. Devuelve null si terminó, o el motivo si se canceló o no hubo
    /// actividad durante el tiempo límite. Antes de declararlo colgado consulta si la sesión de restauración sigue
    /// ejecutando una sentencia (p. ej. un índice grande), para no cortar trabajo legítimo sin salida.
    /// </summary>
    private async Task<string?> WaitWithWatchdogAsync(
        Task work,
        string toolName,
        string applicationName,
        Func<DateTime> lastActivity,
        Action touch,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var lastHeartbeatLog = DateTime.UtcNow;

        while (!work.IsCompleted)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return "Operación cancelada por el usuario.";
            }

            if (DateTime.UtcNow - lastHeartbeatLog >= TimeSpan.FromSeconds(30))
            {
                lastHeartbeatLog = DateTime.UtcNow;
                _logger.LogInformation("{Tool} sigue en ejecución. Tiempo transcurrido: {Elapsed:mm\\:ss}. Última actividad hace {Inactive:mm\\:ss} (Límite: {TimeoutSeconds}s)",
                    toolName, DateTime.UtcNow - started, DateTime.UtcNow - lastActivity(), (int)_restoreStallTimeout.TotalSeconds);
            }

            if (DateTime.UtcNow - lastActivity() > _restoreStallTimeout)
            {
                if (await IsRestoreSessionActiveAsync(applicationName))
                {
                    touch();
                }
                else
                {
                    _logger.LogError("{Tool} excedió el tiempo de inactividad de {Seconds} segundos", toolName, _restoreStallTimeout.TotalSeconds);
                    return $"{toolName} se canceló por inactividad prolongada ({_restoreStallTimeout.TotalSeconds} segundos sin respuesta).";
                }
            }

            await Task.WhenAny(work, Task.Delay(500, CancellationToken.None));
        }

        return null;
    }

    private async Task<bool> IsRestoreSessionActiveAsync(string applicationName)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString) { Pooling = false, Timeout = 5, CommandTimeout = 5 };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = @app AND state = 'active')", connection);
            cmd.Parameters.AddWithValue("app", applicationName);
            return (bool)(await cmd.ExecuteScalarAsync() ?? false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo consultar la actividad de la sesión de restauración.");
            return false;
        }
    }

    private Process CreatePgProcess(string toolPath, string arguments, string? applicationName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.EnvironmentVariables["PGPASSWORD"] = _postgresPassword;
        startInfo.EnvironmentVariables["PGCONNECT_TIMEOUT"] = "10";
        if (applicationName != null)
        {
            startInfo.EnvironmentVariables["PGAPPNAME"] = applicationName;
        }

        return new Process { StartInfo = startInfo };
    }

    private static void KillProcess(Process? process)
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch { }
    }

    /// <summary>Quita del TOC las entradas del esquema de mantenimiento (respaldos anteriores sí lo incluían).</summary>
    internal static IEnumerable<string> ExcludeMaintenanceSchema(string tocListing)
    {
        var pattern = new System.Text.RegularExpressions.Regex($@"\b{MaintenanceSchema}\b");
        return tocListing
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith(';') || !pattern.IsMatch(line));
    }

    private async Task TerminateOtherConnectionsAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection.ClearAllPools();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString) { Pooling = false, Timeout = 15, CommandTimeout = 60 };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var terminateSql = @"
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND pid <> pg_backend_pid();";

            await using var termCmd = new NpgsqlCommand(terminateSql, connection);
            await termCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // No es fatal: si alguien conserva un bloqueo, el preámbulo falla por lock_timeout y nada se modifica.
            _logger.LogWarning(ex, "Aviso: No se pudieron terminar algunas conexiones activas previas a la base de datos.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>Ejecuta pg_restore -l leyendo stdout y stderr a la vez (leerlos en serie bloquea con un TOC grande).</summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunPgRestoreListAsync(string pgRestorePath, string backupFilePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pgRestorePath,
            Arguments = $"-l \"{backupFilePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, outputTask.Result, errorTask.Result);
    }

    internal async Task<(bool Valid, string Message)> ValidateBackupArchiveAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var pgRestorePath = FindPgRestorePath();
            if (string.IsNullOrEmpty(pgRestorePath))
            {
                // Si pg_restore no está en el host (ej. pruebas unitarias o ambiente sin herramientas instaladas),
                // verificamos que el archivo al menos exista y no esté vacío
                if (File.Exists(backupFilePath) && new FileInfo(backupFilePath).Length > 0)
                {
                    return (true, "Archivo existente (pg_restore no disponible en PATH para inspección de TOC).");
                }
                return (false, "pg_restore no encontrado y el archivo no existe o está vacío.");
            }

            if (!File.Exists(backupFilePath))
            {
                return (false, $"El archivo no existe: {backupFilePath}");
            }

            // pg_restore -l inspecciona el archivo TOC sin conectarse a la base de datos
            var (exitCode, _, errorOutput) = await RunPgRestoreListAsync(pgRestorePath, backupFilePath, cancellationToken);
            if (exitCode != 0)
            {
                _logger.LogError("Validación de archivo de respaldo falló con código {ExitCode}: {Error}", exitCode, errorOutput);
                return (false, $"Archivo de respaldo corrupto o en formato no compatible: {errorOutput.Trim()}");
            }

            _logger.LogInformation("Archivo de respaldo validado exitosamente con pg_restore -l.");
            return (true, "Archivo de respaldo válido");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción durante la validación del archivo de respaldo");
            return (false, $"Error validando archivo: {ex.Message}");
        }
    }

    private async Task<(bool Valid, string Message)> ValidateDatabasePostRestoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            NpgsqlConnection.ClearAllPools();
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Pooling = false,
                Timeout = 15,
                CommandTimeout = 30
            };

            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Verificar tablas esenciales
            var checkSql = @"
                SELECT COUNT(*) FROM information_schema.tables 
                WHERE table_schema = 'public' 
                  AND table_name IN ('AspNetUsers', 'Employees', 'AttendanceRecords');";

            await using var cmd = new NpgsqlCommand(checkSql, connection);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            if (count < 2) // Al menos deben existir las tablas principales
            {
                return (false, $"Verificación post-restauración fallida: Solo se encontraron {count} tablas esperadas en public.");
            }

            _logger.LogInformation("Verificación post-restauración exitosa ({Count} tablas verificadas).", count);
            return (true, "Base de datos verificada exitosamente.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verificando esquema tras la restauración.");
            return (false, $"Error verificando esquema: {ex.Message}");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
        }
    }

    internal string FindPgToolPath(string baseToolName)
    {
        var isWindows = OperatingSystem.IsWindows();
        var toolName = isWindows ? $"{baseToolName}.exe" : baseToolName;

        // 1. Verificar si está en la configuración (soporta "Backup:PostgresBinPath" o "DatabaseBackup:PostgresBinPath")
        var configPath = _configuration["Backup:PostgresBinPath"] ?? _configuration["DatabaseBackup:PostgresBinPath"];
        if (!string.IsNullOrEmpty(configPath))
        {
            var fullPath = Path.Combine(configPath, toolName);
            if (File.Exists(fullPath)) return fullPath;
        }

        // 2. Verificar rutas de instalación estándar
        if (isWindows)
        {
            var standardVersions = new[] { "17", "16", "15", "14", "13", "12" };
            foreach (var version in standardVersions)
            {
                var standardPath = Path.Combine(@"C:\Program Files\PostgreSQL", version, "bin", toolName);
                if (File.Exists(standardPath)) return standardPath;

                var x86Path = Path.Combine(@"C:\Program Files (x86)\PostgreSQL", version, "bin", toolName);
                if (File.Exists(x86Path)) return x86Path;
            }
        }
        else
        {
            var standardPaths = new[] { "/usr/bin", "/usr/local/bin", "/usr/local/pgsql/bin" };
            foreach (var path in standardPaths)
            {
                var fullPath = Path.Combine(path, toolName);
                if (File.Exists(fullPath)) return fullPath;
            }
        }

        // 3. Buscar en el PATH del sistema
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path.Trim(), toolName);
                if (File.Exists(fullPath)) return fullPath;
            }
        }

        return string.Empty;
    }

    private string? FindPgDumpPath()
    {
        var path = FindPgToolPath("pg_dump");
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private string? FindPgRestorePath()
    {
        var path = FindPgToolPath("pg_restore");
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// psql debe ser de la misma instalación que pg_restore: los scripts recientes usan metacomandos
    /// (\restrict) que un psql más antiguo no reconoce.
    /// </summary>
    private string? FindPsqlPath(string pgRestorePath)
    {
        var sibling = Path.Combine(Path.GetDirectoryName(pgRestorePath) ?? string.Empty, OperatingSystem.IsWindows() ? "psql.exe" : "psql");
        if (File.Exists(sibling)) return sibling;

        var path = FindPgToolPath("psql");
        return string.IsNullOrEmpty(path) ? null : path;
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
