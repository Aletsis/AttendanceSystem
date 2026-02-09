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
    /// Restaura desde un archivo de respaldo
    /// </summary>
    Task<RestoreResultDto> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

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
}
