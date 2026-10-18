namespace AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

public enum ConsolidationStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Representa una intención durable de consolidación de asistencia diaria
/// generada a partir de eventos de ingesta (ADMS, Cloud Sync, External API, Descargas SDK/Hikvision).
/// </summary>
public sealed class AttendanceConsolidationRequest : Entity<Guid>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public ConsolidationStatus Status { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    private AttendanceConsolidationRequest() { } // Para EF Core

    public static AttendanceConsolidationRequest Create(
        EmployeeId employeeId,
        DateTime startDate,
        DateTime endDate,
        DateTime? requestedAt = null)
    {
        return new AttendanceConsolidationRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            Status = ConsolidationStatus.Pending,
            RequestedAt = requestedAt ?? DateTime.UtcNow,
            RetryCount = 0
        };
    }

    public void ExpandRange(DateTime start, DateTime end)
    {
        if (start.Date < StartDate)
        {
            StartDate = start.Date;
        }
        if (end.Date > EndDate)
        {
            EndDate = end.Date;
        }
        if (Status == ConsolidationStatus.Completed || Status == ConsolidationStatus.Failed)
        {
            Status = ConsolidationStatus.Pending;
        }
    }

    public void MarkAsProcessing()
    {
        Status = ConsolidationStatus.Processing;
    }

    public void MarkAsCompleted()
    {
        Status = ConsolidationStatus.Completed;
        ProcessedAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkAsFailed(string error)
    {
        Status = ConsolidationStatus.Failed;
        ProcessedAt = DateTime.UtcNow;
        ErrorMessage = error;
        RetryCount++;
    }

    public void Requeue()
    {
        Status = ConsolidationStatus.Pending;
    }
}
