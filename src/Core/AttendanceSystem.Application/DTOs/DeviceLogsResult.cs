namespace AttendanceSystem.Application.DTOs;

public enum DeviceReadStatus
{
    SuccessWithData = 1,
    SuccessEmpty = 2,
    Partial = 3,
    Failed = 4,
    Cancelled = 5
}

public sealed record DeviceLogsResult(
    DeviceReadStatus Status,
    IReadOnlyList<RawAttendanceRecord> Records,
    string? ErrorMessage = null,
    int? TotalRecordsOnDevice = null)
{
    public bool IsSuccess => Status == DeviceReadStatus.SuccessWithData || Status == DeviceReadStatus.SuccessEmpty;
    public bool HasRecords => Records.Count > 0;

    public static DeviceLogsResult Success(IReadOnlyList<RawAttendanceRecord> records, int? totalCount = null) =>
        new(records.Count > 0 ? DeviceReadStatus.SuccessWithData : DeviceReadStatus.SuccessEmpty, records, null, totalCount ?? records.Count);

    public static DeviceLogsResult Empty(int? totalCount = null) =>
        new(DeviceReadStatus.SuccessEmpty, Array.Empty<RawAttendanceRecord>(), null, totalCount ?? 0);

    public static DeviceLogsResult Partial(IReadOnlyList<RawAttendanceRecord> records, string errorMessage, int? totalCount = null) =>
        new(DeviceReadStatus.Partial, records, errorMessage, totalCount);

    public static DeviceLogsResult Failed(string errorMessage) =>
        new(DeviceReadStatus.Failed, Array.Empty<RawAttendanceRecord>(), errorMessage);

    public static DeviceLogsResult Cancelled(string? message = null) =>
        new(DeviceReadStatus.Cancelled, Array.Empty<RawAttendanceRecord>(), message ?? "Operación cancelada");
}
