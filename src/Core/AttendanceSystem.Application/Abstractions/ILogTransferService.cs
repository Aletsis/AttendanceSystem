using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Abstractions;

public interface ILogTransferService
{
    /// <summary>
    /// Transfiere un log de asistencia de empleado externo hacia la base de datos central en la nube.
    /// </summary>
    Task<Result<bool>> TransferLogAsync(
        string branchCode,
        string employeeId,
        DateTime checkTime,
        int verifyMethod,
        int checkType,
        string? sourceDevice = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reintenta la transferencia de todos los logs pendientes o fallidos almacenados localmente.
    /// </summary>
    Task<Result<int>> RetryPendingTransfersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prueba la conexión a la base de datos en la nube.
    /// </summary>
    Task<Result<bool>> TestConnectionAsync(string? connectionString = null, CancellationToken cancellationToken = default);
}
