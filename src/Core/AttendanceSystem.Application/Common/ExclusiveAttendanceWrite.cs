using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Common;

/// <summary>
/// Protocolo común de escritura sobre resultados de asistencia (F03-02 / F03-03 / F03-07), usado igual
/// por Blazor, WPF y los trabajos en segundo plano: transacción, bloqueo por empleado dentro de ella,
/// trabajo, y confirmación sólo si el resultado lo permite. Ante fallo o cancelación revierte con un
/// token no cancelado y descarta el seguimiento de cambios; los bloqueos se liberan con la transacción.
/// </summary>
public static class ExclusiveAttendanceWrite
{
    public static async Task<T> RunAsync<T>(
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        IEnumerable<EmployeeId> employeeIds,
        Func<Task<T>> work,
        Func<T, bool> shouldCommit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await lockService.AcquireAsync(employeeIds, cancellationToken);
            var result = await work();

            if (shouldCommit(result))
            {
                await unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            else
            {
                await RollbackQuietlyAsync(unitOfWork);
                unitOfWork.DiscardChanges();
            }

            return result;
        }
        catch
        {
            await RollbackQuietlyAsync(unitOfWork);
            unitOfWork.DiscardChanges();
            throw;
        }
    }

    public static Task<Result> RunAsync(
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        IEnumerable<EmployeeId> employeeIds,
        Func<Task<Result>> work,
        CancellationToken cancellationToken) =>
        RunAsync(unitOfWork, lockService, employeeIds, work, r => r.IsSuccess, cancellationToken);

    /// <summary>
    /// Revierte sin propagar la cancelación: el rollback debe ocurrir aunque el token ya esté cancelado.
    /// Si la transacción ya terminó (p. ej. conexión perdida) no hay nada que revertir.
    /// </summary>
    public static async Task RollbackQuietlyAsync(IUnitOfWork unitOfWork)
    {
        try
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
        catch
        {
            // La transacción ya no está activa; el servidor la revirtió al cerrar la conexión.
        }
    }
}
