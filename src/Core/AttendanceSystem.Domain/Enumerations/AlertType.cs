namespace AttendanceSystem.Domain.Enumerations;

public enum AlertType
{
    CriticalPositionAbsence = 1,
    DeviceOffline = 2,
    DeviceMemoryFull = 3,
    DeviceClockDrift = 4,
    PunchQuarantined = 5,
    SystemError = 99
}
