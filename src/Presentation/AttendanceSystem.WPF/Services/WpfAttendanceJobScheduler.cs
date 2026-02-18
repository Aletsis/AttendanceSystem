using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.WPF.Services
{
    public class WpfAttendanceJobScheduler : IAttendanceJobScheduler
    {
        public void ScheduleAutoDownload(TimeSpan timeOfDay)
        {
            // No-op for WPF Client
        }

        public void DisableAutoDownload()
        {
            // No-op for WPF Client
        }
    }
}
