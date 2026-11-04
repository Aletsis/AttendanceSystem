using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.WPF.Services
{
    public class WpfAttendanceJobScheduler : IAttendanceJobScheduler
    {
        public void ScheduleAutoDownload(TimeSpan timeOfDay, string timeZoneId)
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

        public void ScheduleAutoBackup(TimeSpan timeOfDay, string timeZoneId)
        {
            // No-op
        }

        public void DisableAutoBackup()
        {
            // No-op
        }

        public void ScheduleAutoReport(TimeSpan timeOfDay, string timeZoneId)
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

        public void ScheduleCloudSync()
        {
            // No-op for WPF Client
        }

        public void ScheduleScheduleIntegrityCheck()
        {
            // No-op for WPF Client - Handled by server
        }

        public void ScheduleDailyCalculation(string timeZoneId)
        {
            // No-op for WPF Client - Handled by server
        }

        public void EnqueueBiometricSync(string deviceId, IReadOnlyCollection<string> employeeIds)
        {
            // No-op for WPF Client - Handled by server
        }

        public void EnqueueDeviceCatchUp(string deviceId)
        {
            // No-op for WPF Client - Handled by server
        }
    }
}
