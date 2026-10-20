namespace AttendanceSystem.Application.Abstractions;

using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Exclusión entre procesos para escrituras que afectan resultados de asistencia de un empleado
/// (recálculo, asignación manual, ajustes de descanso o turno). F03-02 / D-04.
/// <para>
/// Los bloqueos son de transacción: deben solicitarse dentro de una transacción abierta con
/// <see cref="IUnitOfWork.BeginTransactionAsync"/> y se liberan solos al confirmar, revertir o
/// perder la conexión, por lo que una cancelación o la muerte del proceso no dejan bloqueos permanentes.
/// Se adquieren en orden determinista de clave para evitar interbloqueos.
/// </para>
/// </summary>
public interface IAttendanceCalculationLockService
{
    /// <summary>
    /// Adquiere los bloqueos de los empleados indicados en la transacción activa. Espera como máximo
    /// el tiempo configurado; al agotarse lanza una excepción que <see cref="IPersistenceFailureClassifier"/>
    /// clasifica como <see cref="PersistenceFailureKind.LockTimeout"/>.
    /// </summary>
    Task AcquireAsync(IEnumerable<EmployeeId> employeeIds, CancellationToken cancellationToken = default);
}
