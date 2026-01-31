using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;

namespace AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;

public sealed class DownloadLog : AggregateRoot<DownloadLogId>
{
    public DeviceId DeviceId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool IsSuccessful { get; private set; }
    public int TotalRecordsDownloaded { get; private set; }
    public int NewRecordsAdded { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DownloadType DownloadType { get; private set; }
    public string? InitiatedByUserId { get; private set; } // Null para descargas automáticas
    public string? InitiatedByUserName { get; private set; }
    public DateTime? FromDate { get; private set; }
    public DateTime? ToDate { get; private set; }
    public int DurationMs { get; private set; }

    private DownloadLog(
        DownloadLogId id,
        DeviceId deviceId,
        DateTime startedAt,
        DownloadType downloadType,
        string? initiatedByUserId,
        string? initiatedByUserName,
        DateTime? fromDate,
        DateTime? toDate)
    {
        Id = id;
        DeviceId = deviceId;
        StartedAt = startedAt;
        DownloadType = downloadType;
        InitiatedByUserId = initiatedByUserId;
        InitiatedByUserName = initiatedByUserName;
        FromDate = fromDate;
        ToDate = toDate;
        IsSuccessful = false;
    }

    public static DownloadLog Create(
        DeviceId deviceId,
        DownloadType downloadType,
        string? initiatedByUserId = null,
        string? initiatedByUserName = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        return new DownloadLog(
            DownloadLogId.CreateUnique(),
            deviceId,
            DateTime.UtcNow,
            downloadType,
            initiatedByUserId,
            initiatedByUserName,
            fromDate,
            toDate);
    }

    public void MarkAsSuccessful(int totalRecordsDownloaded, int newRecordsAdded)
    {
        CompletedAt = DateTime.UtcNow;
        IsSuccessful = true;
        TotalRecordsDownloaded = totalRecordsDownloaded;
        NewRecordsAdded = newRecordsAdded;
        DurationMs = (int)(CompletedAt.Value - StartedAt).TotalMilliseconds;
    }

    public void MarkAsFailed(string errorMessage)
    {
        CompletedAt = DateTime.UtcNow;
        IsSuccessful = false;
        ErrorMessage = errorMessage;
        DurationMs = (int)(CompletedAt.Value - StartedAt).TotalMilliseconds;
    }

    // Constructor privado para EF Core
    private DownloadLog() 
    {
        DeviceId = null!;
        DownloadType = DownloadType.Automatic;
    }
}

public sealed record DownloadLogId(Guid Value)
{
    public static DownloadLogId CreateUnique() => new(Guid.NewGuid());
    public static DownloadLogId From(Guid value) => new(value);
}

public enum DownloadType
{
    Manual = 1,
    Automatic = 2
}
