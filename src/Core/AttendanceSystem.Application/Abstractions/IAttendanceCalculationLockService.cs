namespace AttendanceSystem.Application.Abstractions;

using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Provee exclusión coordinada para operaciones de recálculo y consolidación de asistencia diaria
/// por empleado, adquiriendo bloqueos en orden determinista para prevenir interbloqueos (deadlocks).
/// </summary>
public interface IAttendanceCalculationLockService
{
    Task ExecuteWithLockAsync(
        IEnumerable<EmployeeId> employeeIds,
        Func<Task> action,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteWithLockAsync<T>(
        IEnumerable<EmployeeId> employeeIds,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);
}
