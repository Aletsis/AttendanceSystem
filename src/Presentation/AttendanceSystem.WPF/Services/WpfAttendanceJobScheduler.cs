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

        public void ScheduleCriticalAbsenceCheck()
        {
            // No-op for WPF Client
        }

        public void ScheduleAutoBackup(TimeSpan timeOfDay)
        {
            // No-op
        }

        public void DisableAutoBackup()
        {
            // No-op
        }

        public void ScheduleAutoReport(TimeSpan timeOfDay)
        {
            // No-op
        }

        public void DisableAutoReport()
        {
            // No-op
        }

        public void ScheduleDeviceHeartbeat()
        {
            // No-op for WPF Client
        }

        public void EnqueueBiometricSync(string deviceId, string employeeId)
        {
            // No-op for WPF Client - Handled by server
        }

        public void EnqueueAttendanceProcessing(DateTime startDate, DateTime endDate, string? employeeId = null)
        {
            // No-op for WPF Client - Handled by server
        }
    }
}
