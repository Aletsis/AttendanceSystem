namespace AttendanceSystem.Domain.Events;

/// <param name="WorkDate">Fecha laboral civil del marcaje. Nula en mensajes anteriores a su incorporación.</param>
public sealed record AttendanceRecordedEvent(
    AttendanceRecordId RecordId,
    EmployeeId EmployeeId,
    DateTime CheckTime,
    CheckType CheckType,
    DateOnly? WorkDate = null) : DomainEvent(DateTime.UtcNow);

public sealed record AttendanceProcessedEvent(
    AttendanceRecordId RecordId,
    EmployeeId EmployeeId,
    DateTime ProcessedAt) : DomainEvent(DateTime.UtcNow);

public sealed record AttendanceAnomalyDetectedEvent(
    AttendanceRecordId RecordId,
    EmployeeId EmployeeId,
    DateTime CheckTime,
    string Reason) : DomainEvent(DateTime.UtcNow);

public sealed record OutOfHoursCheckDetectedEvent(
    AttendanceRecordId RecordId,
    EmployeeId EmployeeId,
    DateTime CheckTime) : DomainEvent(DateTime.UtcNow);
