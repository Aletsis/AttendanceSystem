namespace AttendanceSystem.Domain.Aggregates.DeviceAggregate;

public enum AdmsCommandStatus
{
    Pending = 1,
    Sent = 2,
    Confirmed = 3,
    Failed = 4,
    Expired = 5
}
