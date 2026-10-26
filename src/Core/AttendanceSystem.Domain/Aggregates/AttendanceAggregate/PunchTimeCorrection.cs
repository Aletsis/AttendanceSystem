namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

/// <summary>
/// Reinterpretación auditada de los marcajes de un reloj en un rango de fechas: con la zona correcta (el reloj marcaba
/// bien pero se interpretó en otra zona) y/o con un desfase (el reloj marcaba una hora equivocada). Caso 14: el marcaje
/// original no se modifica; se anula y lo reemplaza un marcaje corregido.
/// </summary>
public sealed class PunchTimeCorrection : AggregateRoot<Guid>
{
    private readonly List<PunchTimeCorrectionEntry> _entries = new();

    public DeviceId DeviceId { get; private set; } = null!;
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public string? TimeZoneId { get; private set; }
    public int OffsetMinutes { get; private set; }
    public string Reason { get; private set; } = null!;
    public string AppliedBy { get; private set; } = null!;
    public DateTime AppliedAtUtc { get; private set; }
    public int SkippedCount { get; private set; }
    public IReadOnlyList<PunchTimeCorrectionEntry> Entries => _entries;

    private PunchTimeCorrection() { }

    public static PunchTimeCorrection Create(DeviceId deviceId, DateOnly fromDate, DateOnly toDate, string? timeZoneId, int offsetMinutes, string reason, string appliedBy)
    {
        if (toDate < fromDate) throw new DomainException("El rango de fechas es inválido");
        if (string.IsNullOrWhiteSpace(timeZoneId) && offsetMinutes == 0) throw new DomainException("Indique la zona correcta o un desfase");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Indique el motivo de la corrección");
        return new PunchTimeCorrection
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            FromDate = fromDate,
            ToDate = toDate,
            TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim(),
            OffsetMinutes = offsetMinutes,
            Reason = reason.Trim(),
            AppliedBy = appliedBy,
            AppliedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>Crea el marcaje corregido, anula el original apuntando a él y registra ambos.</summary>
    public AttendanceRecord Apply(AttendanceRecord record, DateTime newCheckTimeUtc, DateTime newCivilTime, DateOnly newWorkDate, string newTimeZoneId)
    {
        var corrected = record.CorrectedCopy(newCheckTimeUtc, newCivilTime, newWorkDate, newTimeZoneId);
        record.Void(AppliedBy, $"Corrección de hora del reloj: {Reason}", corrected.Id, AppliedAtUtc);
        _entries.Add(new PunchTimeCorrectionEntry(Id, record, corrected));
        return corrected;
    }

    public void Skip() => SkippedCount++;
}

public sealed class PunchTimeCorrectionEntry
{
    public Guid Id { get; private set; }
    public Guid CorrectionId { get; private set; }
    public AttendanceRecordId AttendanceRecordId { get; private set; } = null!;
    public string EmployeeId { get; private set; } = null!;
    public DateTime OldCheckTimeUtc { get; private set; }
    public DateTime? OldCivilTime { get; private set; }
    public DateOnly? OldWorkDate { get; private set; }
    public string? OldTimeZoneId { get; private set; }
    public DateTime NewCheckTimeUtc { get; private set; }
    public DateTime NewCivilTime { get; private set; }
    public DateOnly NewWorkDate { get; private set; }
    public string NewTimeZoneId { get; private set; } = null!;
    /// <summary>Marcaje corregido que reemplaza al original (caso 14); nulo en correcciones anteriores.</summary>
    public AttendanceRecordId? NewRecordId { get; private set; }

    private PunchTimeCorrectionEntry() { }

    internal PunchTimeCorrectionEntry(Guid correctionId, AttendanceRecord original, AttendanceRecord corrected)
    {
        Id = Guid.NewGuid();
        CorrectionId = correctionId;
        AttendanceRecordId = original.Id;
        EmployeeId = original.EmployeeId.Value;
        OldCheckTimeUtc = original.CheckTime;
        OldCivilTime = original.OriginalCheckTime;
        OldWorkDate = original.WorkDate;
        OldTimeZoneId = original.TimeZoneId;
        NewCheckTimeUtc = corrected.CheckTime;
        NewCivilTime = DateTime.SpecifyKind(corrected.OriginalCheckTime!.Value, DateTimeKind.Unspecified);
        NewWorkDate = corrected.WorkDate!.Value;
        NewTimeZoneId = corrected.TimeZoneId!;
        NewRecordId = corrected.Id;
    }
}
