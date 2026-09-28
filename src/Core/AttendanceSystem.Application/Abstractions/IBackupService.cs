using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Abstractions;

public interface IBackupService
{
    /// <summary>
    /// Crea un respaldo completo de la base de datos y archivos de configuración
    /// </summary>
    Task<BackupResultDto> CreateFullBackupAsync(string? description = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un respaldo solo de la base de datos
    /// </summary>
    Task<BackupResultDto> CreateDatabaseBackupAsync(string? description = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restaura desde un archivo de respaldo con reporte opcional de progreso y logs en tiempo real
    /// </summary>
    Task<RestoreResultDto> RestoreBackupAsync(string backupFilePath, IProgress<RestoreProgressReport>? progress = null, CancellationToken cancellationToken = default);


    /// <summary>
    /// Lista todos los respaldos disponibles
    /// </summary>
    Task<IEnumerable<BackupDto>> GetAvailableBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina un archivo de respaldo
    /// </summary>
    Task<bool> DeleteBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica la integridad de un archivo de respaldo
    /// </summary>
    Task<bool> ValidateBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda un archivo de respaldo subido en el directorio de respaldos y valida su integridad
    /// </summary>
    Task<BackupResultDto> UploadBackupAsync(string fileName, Stream contentStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la ruta física segura de un archivo de respaldo dado su nombre
    /// </summary>
    Task<string?> GetBackupFilePathAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purga respaldos antiguos conservando los N más recientes
    /// </summary>
    Task<int> PruneOldBackupsAsync(int? keepCount = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene información de la conexión de base de datos activa
    /// </summary>
    DatabaseConnectionInfoDto GetDatabaseConnectionInfo();
}

