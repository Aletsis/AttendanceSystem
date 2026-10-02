namespace AttendanceSystem.Domain.Aggregates.DeviceAggregate;

public record DeviceHardwareInfo(
    string? SerialNumber,
    string? FirmwareVersion,
    string? Platform,
    int? UserCount,
    int? FingerprintCount,
    int? FaceCount,
    int? AttendanceRecordCount,
    int? UserCapacity,
    int? FingerprintCapacity,
    int? FaceCapacity,
    int? AttendanceRecordCapacity,
    string? PushVersion = null,
    string? SdkVersion = null,
    string? MacAddress = null)
{
    public static DeviceHardwareInfo Empty => new(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
