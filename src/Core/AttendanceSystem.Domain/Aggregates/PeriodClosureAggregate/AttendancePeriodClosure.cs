namespace AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;

/// <summary>
/// Caso 17: cierre de un periodo de asistencia (semana, quincena o mes según la configuración) para todos los
/// empleados. Mientras está vigente, ningún día del periodo se recalcula ni acepta ajustes. Reabrirlo lo deja abierto
/// (con autor y motivo); un cierre nunca se borra.
/// </summary>
public sealed class AttendancePeriodClosure : AggregateRoot<Guid>
{
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public DateTime ClosedAt { get; private set; }
    public string ClosedBy { get; private set; } = null!;
    public bool Automatic { get; private set; }
    public DateTime? ReopenedAt { get; private set; }
    public string? ReopenedBy { get; private set; }
    public string? ReopenReason { get; private set; }

    public bool IsActive => !ReopenedAt.HasValue;

    private AttendancePeriodClosure() { }

    public static AttendancePeriodClosure Close(DateTime startDate, DateTime endDate, string closedBy, bool automatic, DateTime? closedAt = null)
    {
        if (endDate.Date < startDate.Date) throw new DomainException("El periodo es inválido");
        return new AttendancePeriodClosure
        {
            Id = Guid.NewGuid(),
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            ClosedAt = closedAt ?? DateTime.UtcNow,
            ClosedBy = string.IsNullOrWhiteSpace(closedBy) ? "Sistema" : closedBy,
            Automatic = automatic
        };
    }

    public bool Contains(DateTime date) => date.Date >= StartDate && date.Date <= EndDate;

    public void Reopen(string reopenedBy, string reason, DateTime? reopenedAt = null)
    {
        if (!IsActive) throw new DomainException("El periodo ya está abierto");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Indique el motivo para reabrir el periodo");
        ReopenedBy = string.IsNullOrWhiteSpace(reopenedBy) ? "Sistema" : reopenedBy;
        ReopenReason = reason.Trim();
        ReopenedAt = reopenedAt ?? DateTime.UtcNow;
    }
}
