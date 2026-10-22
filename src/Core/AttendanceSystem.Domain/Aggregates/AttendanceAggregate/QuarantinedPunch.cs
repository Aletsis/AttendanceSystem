using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

public enum QuarantineStatus
{
    Pending = 1,
    Released = 2,
    Discarded = 3
}

/// <summary>
/// Marcaje recibido con una hora imposible (<see cref="PunchTimePlausibility"/>). No participa en ningún cálculo
/// hasta que se libera (con la hora corregida) o se descarta; ambas decisiones quedan auditadas.
/// </summary>
public sealed class QuarantinedPunch : AggregateRoot<Guid>
{
    public string EmployeeId { get; private set; } = null!;
    public DeviceId DeviceId { get; private set; } = null!;
    /// <summary>Hora civil que reportó el reloj.</summary>
    public DateTime ReportedTime { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public int VerifyMethodCode { get; private set; }
    public int CheckTypeCode { get; private set; }
    public string Source { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public DateTime ReceivedAtUtc { get; private set; }
    public QuarantineStatus Status { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? ResolvedBy { get; private set; }
    public string? ResolutionNote { get; private set; }
    /// <summary>Hora civil con la que se liberó (reportada + desfase aplicado).</summary>
    public DateTime? ReleasedTime { get; private set; }
    public AttendanceRecordId? ReleasedRecordId { get; private set; }

    private QuarantinedPunch() { }

    public static QuarantinedPunch Create(string employeeId, DeviceId deviceId, DateTime reportedTime, string timeZoneId,
        int verifyMethodCode, int checkTypeCode, string source, string reason)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) throw new DomainException("El marcaje en cuarentena requiere empleado");
        return new QuarantinedPunch
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId.Trim(),
            DeviceId = deviceId,
            ReportedTime = DateTime.SpecifyKind(reportedTime, DateTimeKind.Unspecified),
            TimeZoneId = timeZoneId,
            VerifyMethodCode = verifyMethodCode,
            CheckTypeCode = checkTypeCode,
            Source = source,
            Reason = reason,
            ReceivedAtUtc = DateTime.UtcNow,
            Status = QuarantineStatus.Pending
        };
    }

    public void Release(DateTime correctedTime, AttendanceRecordId recordId, string user, string note)
    {
        EnsurePending(note);
        Status = QuarantineStatus.Released;
        ReleasedTime = DateTime.SpecifyKind(correctedTime, DateTimeKind.Unspecified);
        ReleasedRecordId = recordId;
        Resolve(user, note);
    }

    /// <summary>El marcaje corregido ya existía (otro reloj o una descarga previa): se cierra sin duplicarlo.</summary>
    public void ReleaseAsDuplicate(DateTime correctedTime, string user, string note)
    {
        EnsurePending(note);
        Status = QuarantineStatus.Released;
        ReleasedTime = DateTime.SpecifyKind(correctedTime, DateTimeKind.Unspecified);
        Resolve(user, note);
    }

    public void Discard(string user, string note)
    {
        EnsurePending(note);
        Status = QuarantineStatus.Discarded;
        Resolve(user, note);
    }

    /// <summary>Valida antes de cambiar nada: una decisión rechazada deja el marcaje pendiente.</summary>
    private void EnsurePending(string note)
    {
        if (Status != QuarantineStatus.Pending) throw new DomainException("El marcaje ya fue resuelto");
        if (string.IsNullOrWhiteSpace(note)) throw new DomainException("Indique el motivo de la decisión");
    }

    private void Resolve(string user, string note)
    {
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedBy = user;
        ResolutionNote = note.Trim();
    }
}
