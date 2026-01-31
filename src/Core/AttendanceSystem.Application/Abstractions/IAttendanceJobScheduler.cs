namespace AttendanceSystem.Application.Abstractions;

public interface IAttendanceJobScheduler
{
    void ScheduleAutoDownload(TimeSpan timeOfDay);
    void DisableAutoDownload();
}
