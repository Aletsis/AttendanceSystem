using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using Hangfire;
using MediatR;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;

namespace AttendanceSystem.Infrastructure.Services;

public class HangfireAttendanceJobScheduler : IAttendanceJobScheduler
{
    private const string JobId = "download-attendance-daily";
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireAttendanceJobScheduler(
        IRecurringJobManager recurringJobManager,
        IBackgroundJobClient backgroundJobClient)
    {
        _recurringJobManager = recurringJobManager;
        _backgroundJobClient = backgroundJobClient;
    }

    public void ScheduleAutoDownload(TimeSpan timeOfDay)
    {
        // Cron: Minute Hour * * *
        // Example: 14:30 -> "30 14 * * *"
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";
        
        // Use the server's local timezone to match the user's expected time exactly
        var timeZone = TimeZoneInfo.Local;
        
        var options = new RecurringJobOptions
        {
            TimeZone = timeZone
        };

        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            JobId, 
            jobs => jobs.DownloadFromAllDevices(), 
            cron,
            options);
    }

    public void DisableAutoDownload()
    {
        _recurringJobManager.RemoveIfExists(JobId);
    }

    public void ScheduleCriticalAbsenceCheck()
    {
        // Every 15 minutes: "*/15 * * * *"
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            "check-critical-absences",
            jobs => jobs.CheckCriticalAbsences(),
            "*/15 * * * *");
    }
    public void ScheduleAutoBackup(TimeSpan timeOfDay)
    {
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";
        var timeZone = TimeZoneInfo.Local;
        
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            "automated-database-backup", 
            jobs => jobs.PerformAutoBackup(), 
            cron,
            new RecurringJobOptions { TimeZone = timeZone });
    }

    public void DisableAutoBackup()
    {
        _recurringJobManager.RemoveIfExists("automated-database-backup");
    }

    public void ScheduleAutoReport(TimeSpan timeOfDay)
    {
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";
        var timeZone = TimeZoneInfo.Local;
        
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            "automated-daily-report", 
            jobs => jobs.SendAutoReport(), 
            cron,
            new RecurringJobOptions { TimeZone = timeZone });
    }

    public void DisableAutoReport()
    {
        _recurringJobManager.RemoveIfExists("automated-daily-report");
    }
    public void ScheduleDeviceHeartbeat()
    {
        // Every 10 minutes
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            "device-heartbeat-monitor",
            jobs => jobs.MonitorDeviceHealth(),
            "*/10 * * * *");
    }

    public void EnqueueBiometricSync(string deviceId, string employeeId)
    {
        _backgroundJobClient.Enqueue<AttendanceJobs>(
            jobs => jobs.SyncEmployeeBiometrics(deviceId, employeeId));
    }

    public void EnqueueAttendanceProcessing(DateTime startDate, DateTime endDate, string? employeeId = null)
    {
        _backgroundJobClient.Enqueue<AttendanceJobs>(
            jobs => jobs.ProcessAttendance(startDate, endDate, employeeId));
    }
}

public class AttendanceJobs
{
    private readonly IMediator _mediator;

    public AttendanceJobs(IMediator mediator)
    {
        _mediator = mediator;
    }

    [JobDisplayName("Download Logs from All Devices")]
    public async Task DownloadFromAllDevices()
    {
        await _mediator.Send(new DownloadFromAllDevicesCommand(null, null));
    }

    [JobDisplayName("Check for Critical Position Absences")]
    public async Task CheckCriticalAbsences()
    {
        await _mediator.Send(new CheckCriticalAbsencesCommand());
    }

    [JobDisplayName("Automated Database Backup")]
    public async Task PerformAutoBackup()
    {
        await _mediator.Send(new AttendanceSystem.Application.Features.Backup.Commands.CreateBackupCommand("Full", "Respaldo Automático Programado"));
    }

    [JobDisplayName("Automated Daily Report")]
    public async Task SendAutoReport()
    {
        await _mediator.Send(new AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport.SendDailyReportCommand());
    }

    [JobDisplayName("Monitor Device Health (Heartbeat)")]
    public async Task MonitorDeviceHealth()
    {
        await _mediator.Send(new AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth.MonitorDeviceHealthCommand());
    }

    [JobDisplayName("Sync Biometrics for Employee {1} from Device {0}")]
    public async Task SyncEmployeeBiometrics(string deviceId, string employeeId)
    {
        await _mediator.Send(new AttendanceSystem.Application.Features.Employees.Commands.SyncEmployeeBiometrics.SyncEmployeeBiometricsCommand(deviceId, employeeId));
    }

    [JobDisplayName("Process Attendance for Employee {2} from {0:yyyy-MM-dd} to {1:yyyy-MM-dd}")]
    public async Task ProcessAttendance(DateTime startDate, DateTime endDate, string? employeeId)
    {
        var empId = string.IsNullOrEmpty(employeeId) ? null : AttendanceSystem.Domain.ValueObjects.EmployeeId.From(employeeId);
        await _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.ProcessDailyAttendanceCommand(startDate, endDate, EmployeeId: empId));
    }
}
