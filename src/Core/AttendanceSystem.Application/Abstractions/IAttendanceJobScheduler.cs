namespace AttendanceSystem.Application.Abstractions;

public interface IAttendanceJobScheduler
{
    void ScheduleAutoDownload(TimeSpan timeOfDay);
    void DisableAutoDownload();
    void ScheduleCriticalAbsenceCheck();
    void ScheduleAutoBackup(TimeSpan timeOfDay);
    void DisableAutoBackup();
    void ScheduleAutoReport(TimeSpan timeOfDay);
    void DisableAutoReport();
    void ScheduleDeviceHeartbeat();
    void EnqueueBiometricSync(string deviceId, string employeeId);
    void EnqueueAttendanceProcessing(DateTime startDate, DateTime endDate, string? employeeId = null);
}
