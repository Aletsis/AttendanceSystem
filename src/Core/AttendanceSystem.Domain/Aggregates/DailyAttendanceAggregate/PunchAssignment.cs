namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Posición o destino que ocupa un marcaje dentro de una jornada (F02-01).
/// Las cuatro primeras corresponden a las columnas de <see cref="DailyAttendance"/>;
/// el resto conserva marcajes consumidos por la jornada que no ocupan una posición principal.
/// </summary>
public enum PunchAssignmentRole
{
    CheckIn = 1,
    Block1CheckOut = 2,
    Block2CheckIn = 3,
    CheckOut = 4,

    /// <summary>Marcaje intermedio analizado (comida, permiso temporal).</summary>
    Intermediate = 5,

    /// <summary>Límite de un intervalo MultiInterval distinto de la primera entrada o última salida.</summary>
    IntervalBoundary = 6,

    /// <summary>Doble toque descartado por proximidad (D-02: se audita en base de datos).</summary>
    DiscardedDoubleTap = 7,

    /// <summary>Marcaje consumido por la jornada cuyo significado requiere revisión (p. ej. intermedio sin retorno).</summary>
    PendingReview = 8
}

public enum PunchAssignmentOrigin
{
    Automatic = 1,
    Manual = 2
}

/// <summary>
/// Relación explícita marcaje–jornada–posición (F02-01).
/// La clave es el marcaje: un <see cref="AttendanceRecord"/> sólo puede pertenecer a una jornada
/// y ocupar una posición (C-04). La jornada se identifica por (EmployeeId, WorkDate) para que
/// sobreviva al reemplazo de <see cref="DailyAttendance"/> durante el recálculo (F02-06).
/// Los marcajes sin fila son candidatos libres (estado derivado <c>Pending</c>).
/// </summary>
public sealed class PunchAssignment : Entity<AttendanceRecordId>
{
    public EmployeeId EmployeeId { get; private set; } = null!;

    /// <summary>Fecha laboral de la jornada propietaria (no la del marcaje).</summary>
    public DateOnly WorkDate { get; private set; }

    public PunchAssignmentRole Role { get; private set; }
    public PunchAssignmentOrigin Origin { get; private set; }
    public DateTime AssignedAt { get; private set; }

    public AttendanceRecordId AttendanceRecordId => Id;

    public bool IsPosition => IsPositionRole(Role);

    private PunchAssignment() { } // EF Core

    public static PunchAssignment Create(
        AttendanceRecordId recordId,
        EmployeeId employeeId,
        DateOnly workDate,
        PunchAssignmentRole role,
        PunchAssignmentOrigin origin,
        DateTime? assignedAt = null)
    {
        return new PunchAssignment
        {
            Id = recordId,
            EmployeeId = employeeId,
            WorkDate = workDate,
            Role = role,
            Origin = origin,
            AssignedAt = assignedAt ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Actualiza la asignación en sitio. El propietario nunca cambia: un marcaje no se transfiere entre empleados.
    /// </summary>
    public void Reassign(DateOnly workDate, PunchAssignmentRole role, PunchAssignmentOrigin origin, DateTime? assignedAt = null)
    {
        if (WorkDate == workDate && Role == role && Origin == origin) return;

        WorkDate = workDate;
        Role = role;
        Origin = origin;
        AssignedAt = assignedAt ?? DateTime.UtcNow;
    }

    public bool IsEquivalentTo(PunchAssignment other) =>
        Id == other.Id &&
        EmployeeId == other.EmployeeId &&
        WorkDate == other.WorkDate &&
        Role == other.Role &&
        Origin == other.Origin;

    public static bool IsPositionRole(PunchAssignmentRole role) =>
        role is PunchAssignmentRole.CheckIn
            or PunchAssignmentRole.Block1CheckOut
            or PunchAssignmentRole.Block2CheckIn
            or PunchAssignmentRole.CheckOut;
}
