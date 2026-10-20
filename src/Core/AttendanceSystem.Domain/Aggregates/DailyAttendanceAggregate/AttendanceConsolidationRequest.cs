namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Estados de una solicitud de consolidación.
/// <code>
///            solicitar (upsert, Version+1)
///   ┌──────────────────────────────────────────┐
///   ▼                                          │
/// Pending ──reclamar (lease, ClaimedVersion)──► Processing
///   ▲   ▲                                      │  │  │
///   │   └── versión cambió / fallo reintentable┘  │  │
///   │        / cancelación / lease vencido        │  │
///   │                                             │  └─ éxito con la misma versión ─► Completed
///   └──────── reintento operativo ◄── Failed ◄────┘     (intentos agotados)
///                         (o absorbida por una nueva solicitud ─► Superseded)
/// </code>
/// Pending y Processing son activos: como máximo uno por empleado (índice único parcial).
/// </summary>
public enum ConsolidationStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Superseded = 4
}

/// <summary>
/// Intención durable de consolidar la asistencia de un empleado en un rango de fechas laborales,
/// generada por la ingesta de marcajes. Las transiciones son operaciones atómicas en base de datos
/// (<c>IAttendanceConsolidationRepository</c>); esta entidad sólo refleja su estado persistido.
/// </summary>
public sealed class AttendanceConsolidationRequest : Entity<Guid>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public ConsolidationStatus Status { get; private set; }

    /// <summary>Primera solicitud aún pendiente: define la antigüedad máxima antes de forzar el proceso.</summary>
    public DateTime RequestedAt { get; private set; }

    /// <summary>Última ampliación: define la espera breve para agrupar marcajes.</summary>
    public DateTime LastRequestedAt { get; private set; }

    /// <summary>Se incrementa en cada ampliación; permite completar sólo lo realmente procesado.</summary>
    public int Version { get; private set; }

    public string? ClaimedBy { get; private set; }
    public DateTime? LeaseExpiresAt { get; private set; }
    public int? ClaimedVersion { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>Intentos fallidos consecutivos.</summary>
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    public bool IsActive => Status is ConsolidationStatus.Pending or ConsolidationStatus.Processing;

    private AttendanceConsolidationRequest() { } // Para EF Core

    public static AttendanceConsolidationRequest Create(
        EmployeeId employeeId,
        DateTime startDate,
        DateTime endDate,
        DateTime? requestedAt = null)
    {
        var now = requestedAt ?? DateTime.UtcNow;
        return new AttendanceConsolidationRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            Status = ConsolidationStatus.Pending,
            RequestedAt = now,
            LastRequestedAt = now,
            Version = 1,
            RetryCount = 0
        };
    }
}

/// <summary>Solicitud reclamada por un consumidor: rango y versión capturados al iniciar (F04-06).</summary>
public sealed record ConsolidationClaim(
    Guid Id,
    EmployeeId EmployeeId,
    DateTime StartDate,
    DateTime EndDate,
    int Version,
    int Attempts);

public enum ConsolidationCompletion
{
    /// <summary>Se procesó la versión vigente: la solicitud queda completada.</summary>
    Completed,

    /// <summary>Llegaron cambios durante el cálculo: la solicitud vuelve a Pending con el rango acumulado.</summary>
    Requeued,

    /// <summary>El lease venció y otro consumidor la reclamó: este resultado no se aplica.</summary>
    LeaseLost
}
