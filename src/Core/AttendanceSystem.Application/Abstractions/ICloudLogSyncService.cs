using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Abstractions;

public record CloudLogItemDto(
    Guid Id,
    string BranchCode,
    string EmployeeId,
    DateTime CheckTime,
    int VerifyMethod,
    int CheckType,
    string? SourceDevice,
    DateTime CreatedAt);

public interface ICloudLogSyncService
{
    /// <summary>
    /// Consulta en la base de datos de la nube los logs pendientes de descargar para un código de sucursal.
    /// </summary>
    Task<Result<IReadOnlyList<CloudLogItemDto>>> FetchPendingLogsAsync(string branchCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarga e inserta los logs pendientes desde la nube a la base de datos local para todas las sucursales locales.
    /// </summary>
    Task<Result<int>> SyncPendingLogsForBranchesAsync(IEnumerable<string> branchCodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca los logs indicados como descargados en la base de datos de la nube.
    /// </summary>
    Task<Result<bool>> MarkLogsAsDownloadedAsync(IEnumerable<Guid> logIds, string downloadedByBranch, CancellationToken cancellationToken = default);
}
