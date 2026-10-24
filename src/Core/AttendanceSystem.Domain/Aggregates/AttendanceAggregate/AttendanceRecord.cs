namespace AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

public class AttendanceRecord : AggregateRoot<AttendanceRecordId>
{
    public required EmployeeId EmployeeId { get; set; }
    public required DeviceId DeviceId { get; set; }
    public required DateTime CheckTime { get; set; }
    public required VerifyMethod VerifyMethod { get; set; }
    public required CheckType CheckType { get; set; }
    public required AttendanceStatus Status { get; set; }
    public DownloadLogId? DownloadLogId { get; set; }
    public DateTime? OriginalCheckTime { get; set; }
    public DateOnly? WorkDate { get; set; }
    public string? TimeZoneId { get; set; }
    public string? Source { get; set; }
    public CheckType? OriginalCheckType { get; set; }

    /// <summary>Caso 13: captura manual (origen <see cref="ManualSource"/>): quién la registró, cuándo y por qué.</summary>
    public string? RegisteredBy { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public string? Reason { get; set; }

    public const string ManualSource = "Manual";

    public bool IsManual => Source == ManualSource;

    public void RecordManualCapture(string registeredBy, string reason, DateTime? registeredAt = null)
    {
        RegisteredBy = string.IsNullOrWhiteSpace(registeredBy) ? "Sistema" : registeredBy;
        Reason = reason.Trim();
        RegisteredAt = registeredAt ?? DateTime.UtcNow;
    }

    // Constructor privado - solo se crea mediante Factory
    private AttendanceRecord() { }

    // Factory Method (parte del Aggregate)
    public static AttendanceRecord Create(
        EmployeeId employeeId,
        DeviceId deviceId,
        DateTime checkTime,
        VerifyMethod verifyMethod,
        CheckType checkType,
        DateOnly? workDate = null,
        DateTime? originalCheckTime = null,
        string? timeZoneId = null,
        string? source = null)
    {
        DateTime utcCheckTime;
        DateTime civilOriginal;

        if (checkTime.Kind == DateTimeKind.Utc)
        {
            utcCheckTime = checkTime;
            civilOriginal = originalCheckTime ?? checkTime;
        }
        else
        {
            civilOriginal = originalCheckTime ?? DateTime.SpecifyKind(checkTime, DateTimeKind.Unspecified);
            utcCheckTime = checkTime.Kind == DateTimeKind.Local
                ? checkTime.ToUniversalTime()
                : DateTime.SpecifyKind(checkTime, DateTimeKind.Utc);
        }

        var record = new AttendanceRecord
        {
            Id = AttendanceRecordId.CreateNew(),
            EmployeeId = employeeId,
            DeviceId = deviceId,
            CheckTime = utcCheckTime,
            VerifyMethod = verifyMethod,
            CheckType = checkType,
            Status = AttendanceStatus.Pending,
            OriginalCheckType = checkType,
            OriginalCheckTime = civilOriginal,
            WorkDate = workDate ?? DateOnly.FromDateTime(civilOriginal),
            TimeZoneId = timeZoneId,
            Source = source
        };

        // Regla de negocio: validar horario
        record.ValidateBusinessHours();

        // Levantar evento de dominio
        record.AddDomainEvent(new AttendanceRecordedEvent(
            record.Id,
            record.EmployeeId,
            record.CheckTime,
            record.CheckType,
            record.WorkDate));

        return record;
    }

    // Métodos de negocio (comportamiento del agregado)
    public void MarkAsProcessed()
    {
        // Allow idempotent calls or handle re-processing logic at handler level?
        // Current logic: throws if already processed. 
        // We will stick to the existing rule: explicit transition.
        if (Status == AttendanceStatus.Processed)
            return; // Idempotent is safer for re-processing logic where we might re-save

        Status = AttendanceStatus.Processed;

        AddDomainEvent(new AttendanceProcessedEvent(Id, EmployeeId, CheckTime));
    }

    public void ResetStatus()
    {
        Status = AttendanceStatus.Pending;
        // Optionally clear domain events or add a "Reset" event?
    }

    public void SetInferredType(CheckType type)
    {
        CheckType = type;
    }

    public void MarkAsAnomalous(string reason)
    {
        Status = AttendanceStatus.Anomalous;

        AddDomainEvent(new AttendanceAnomalyDetectedEvent(
            Id, EmployeeId, CheckTime, reason));
    }

    public void SetTimeZone(string timeZoneId)
    {
        TimeZoneId = timeZoneId;
    }

    public void UpdateTimeMetadata(DateTime originalCheckTime, DateOnly workDate, string? timeZoneId)
    {
        OriginalCheckTime = originalCheckTime;
        WorkDate = workDate;
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            TimeZoneId = timeZoneId;
        }
    }

    /// <summary>
    /// Corrige la hora del marcaje (sólo desde <see cref="PunchTimeCorrection"/>, que conserva los valores anteriores):
    /// nuevo instante, hora civil, fecha laboral y zona. Su jornada se recalcula después.
    /// </summary>
    internal void CorrectTime(DateTime checkTimeUtc, DateTime civilTime, DateOnly workDate, string timeZoneId)
    {
        if (checkTimeUtc.Kind != DateTimeKind.Utc) throw new DomainException("El instante corregido debe estar en UTC");
        CheckTime = checkTimeUtc;
        OriginalCheckTime = DateTime.SpecifyKind(civilTime, DateTimeKind.Unspecified);
        WorkDate = workDate;
        TimeZoneId = timeZoneId;
        ResetStatus();
    }

    public DateTime GetCivilTime(string? fallbackTimeZoneId = null)
    {
        if (OriginalCheckTime.HasValue)
            return OriginalCheckTime.Value;

        var tz = ResolveZone(TimeZoneId ?? fallbackTimeZoneId);
        var utc = CheckTime.Kind == DateTimeKind.Utc ? CheckTime : DateTime.SpecifyKind(CheckTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public DateOnly GetWorkDate(string? fallbackTimeZoneId = null)
    {
        if (WorkDate.HasValue)
            return WorkDate.Value;

        return DateOnly.FromDateTime(GetCivilTime(fallbackTimeZoneId));
    }

    private static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            timeZoneId = "America/Mexico_City";

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var winId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
            }
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); } catch { }
            }
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            }
            catch
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId("America/Mexico_City", out var defaultWin))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(defaultWin); } catch { }
                }
                return TimeZoneInfo.Utc;
            }
        }
    }

    // Invariantes (reglas de negocio del dominio)
    private void ValidateBusinessHours()
    {
        var civilHour = (OriginalCheckTime ?? CheckTime).Hour;

        // Ejemplo: alertar si registro fuera de horario laboral en tiempo civil
        if (civilHour < 6 || civilHour > 22)
        {
            AddDomainEvent(new OutOfHoursCheckDetectedEvent(
                Id, EmployeeId, CheckTime));
        }
    }
}
