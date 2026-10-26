namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

/// <summary>
/// Caso 18: un reloj estuvo atrasado (o se le detectó un salto) entre dos horas que él mismo reportó. Cualquier marcaje
/// de ese reloj con hora reportada en el rango que llegue después —en tiempo real, en una descarga o en una
/// re-descarga— va a cuarentena con la corrección sugerida, aunque ya nadie vea el desfase.
/// </summary>
public sealed class DeviceClockIncident : AggregateRoot<Guid>
{
    public DeviceId DeviceId { get; private set; } = null!;
    public DateTime ReportedFrom { get; private set; }
    public DateTime ReportedTo { get; private set; }
    public int? OffsetMinutes { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTime DetectedAtUtc { get; private set; }

    private DeviceClockIncident() { }

    public static DeviceClockIncident Create(DeviceId deviceId, DateTime reportedFrom, DateTime reportedTo, int? offsetMinutes, string reason)
    {
        if (reportedTo < reportedFrom) throw new DomainException("El rango del incidente es inválido");
        return new DeviceClockIncident
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            ReportedFrom = DateTime.SpecifyKind(reportedFrom, DateTimeKind.Unspecified),
            ReportedTo = DateTime.SpecifyKind(reportedTo, DateTimeKind.Unspecified),
            OffsetMinutes = offsetMinutes,
            Reason = reason,
            DetectedAtUtc = DateTime.UtcNow
        };
    }

    public bool Contains(DateTime reportedCivil) => reportedCivil >= ReportedFrom && reportedCivil <= ReportedTo;

    /// <summary>El reloj siguió atrasado: el rango llega hasta la nueva hora reportada.</summary>
    public void ExtendTo(DateTime reportedTo)
    {
        if (reportedTo > ReportedTo) ReportedTo = DateTime.SpecifyKind(reportedTo, DateTimeKind.Unspecified);
    }
}
