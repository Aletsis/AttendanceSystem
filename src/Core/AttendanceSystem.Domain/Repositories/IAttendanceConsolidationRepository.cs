using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Repositories;

public sealed record ConsolidationHealth(
    int Pending,
    int Processing,
    int Failed,
    DateTime? OldestPendingRequestedAt,
    int MaxAttemptsInFlight,
    IReadOnlyList<AttendanceConsolidationRequest> RecentFailures);

/// <summary>
/// Solicitudes de consolidación con transiciones atómicas en base de datos (F04-04 / F04-05 / F04-06).
/// Ninguna operación lee-modifica-escribe en memoria: así dos procesos no pierden ampliaciones ni
/// duplican solicitudes activas.
/// </summary>
public interface IAttendanceConsolidationRepository
{
    /// <summary>
    /// Registra o amplía la solicitud activa del empleado (unión de rangos, Version+1). Absorbe solicitudes
    /// fallidas del mismo empleado para que la nueva actividad las reintente.
    /// </summary>
    Task RequestAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>Igual que <see cref="RequestAsync"/> para varios empleados en una sola sentencia.</summary>
    Task RequestManyAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="RequestAsync"/> para un rango por empleado (se unen los del mismo empleado), en una sola
    /// sentencia por lote (F07-04 / C01).
    /// </summary>
    Task RequestRangesAsync(IReadOnlyCollection<(EmployeeId EmployeeId, DateTime StartDate, DateTime EndDate)> ranges, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclama hasta <paramref name="maxBatchSize"/> solicitudes listas (sin bloquear a otros consumidores):
    /// pendientes cuya espera breve terminó o cuya antigüedad superó el máximo, y en proceso con lease vencido.
    /// </summary>
    Task<IReadOnlyList<ConsolidationClaim>> ClaimAsync(
        string consumerId,
        int maxBatchSize,
        TimeSpan lease,
        TimeSpan quietPeriod,
        TimeSpan maxDelay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// F07-08: igual que <see cref="CompleteAsync"/> para varias solicitudes del mismo consumidor en una sentencia
    /// (una confirmación); cada una con su versión reclamada.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ConsolidationCompletion>> CompleteManyAsync(
        IReadOnlyCollection<(Guid RequestId, int ClaimedVersion)> claims, string consumerId, CancellationToken cancellationToken = default);

    Task<ConsolidationCompletion> CompleteAsync(Guid requestId, string consumerId, int claimedVersion, CancellationToken cancellationToken = default);

    /// <summary>Registra un intento fallido con backoff exponencial; al agotar intentos queda en Failed.</summary>
    Task FailAsync(Guid requestId, string consumerId, string error, int maxAttempts, TimeSpan baseBackoff, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la solicitud a Pending sin contar intento (cancelación, mantenimiento).</summary>
    Task ReleaseAsync(Guid requestId, string consumerId, CancellationToken cancellationToken = default);

    /// <summary>Reintento operativo de solicitudes fallidas (todas o una); devuelve cuántas se reactivaron.</summary>
    Task<int> RetryFailedAsync(Guid? requestId = null, CancellationToken cancellationToken = default);

    Task<AttendanceConsolidationRequest?> GetActiveByEmployeeAsync(EmployeeId employeeId, CancellationToken cancellationToken = default);

    Task<ConsolidationHealth> GetHealthAsync(int recentFailures = 20, CancellationToken cancellationToken = default);

    /// <summary>Elimina solicitudes completadas o absorbidas más antiguas que la retención; conserva las fallidas.</summary>
    Task<int> PurgeFinishedAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}
