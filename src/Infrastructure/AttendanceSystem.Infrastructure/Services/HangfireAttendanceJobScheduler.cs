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

    public void ScheduleCloudSync()
    {
        // Every 5 minutes
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            "cloud-log-sync",
            jobs => jobs.SyncCloudLogs(),
            "*/5 * * * *");
    }

    public void EnqueueBiometricSync(string deviceId, string employeeId)
    {
        _backgroundJobClient.Enqueue<AttendanceJobs>(
            jobs => jobs.SyncEmployeeBiometrics(deviceId, employeeId));
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
        var result = await _mediator.Send(new DownloadFromAllDevicesCommand(null, null));
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Download Logs from All Devices", result.Error);
        }

        if (result.Value.Any(d => !d.Success))
        {
            var failedDevices = result.Value.Where(d => !d.Success).Select(d => $"{d.DeviceId}: {d.ErrorMessage}");
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Download Logs from All Devices",
                $"Fallo en uno o más dispositivos: {string.Join("; ", failedDevices)}");
        }
    }

    [JobDisplayName("Check for Critical Position Absences")]
    public async Task CheckCriticalAbsences()
    {
        var result = await _mediator.Send(new CheckCriticalAbsencesCommand());
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Check for Critical Position Absences", result.Error);
        }
    }

    [JobDisplayName("Automated Database Backup")]
    public async Task PerformAutoBackup()
    {
        var result = await _mediator.Send(new AttendanceSystem.Application.Features.Backup.Commands.CreateBackupCommand("Full", "Respaldo Automático Programado"));
        if (!result.Success)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Automated Database Backup", result.Message);
        }
    }

    [JobDisplayName("Automated Daily Report")]
    public async Task SendAutoReport()
    {
        var result = await _mediator.Send(new AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport.SendDailyReportCommand());
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Automated Daily Report", result.Error);
        }
    }

    [JobDisplayName("Monitor Device Health (Heartbeat)")]
    public async Task MonitorDeviceHealth()
    {
        var result = await _mediator.Send(new AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth.MonitorDeviceHealthCommand());
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Monitor Device Health (Heartbeat)", result.Error);
        }
    }

    [JobDisplayName("Sync Biometrics for Employee {1} from Device {0}")]
    public async Task SyncEmployeeBiometrics(string deviceId, string employeeId)
    {
        var result = await _mediator.Send(new AttendanceSystem.Application.Features.Employees.Commands.SyncEmployeeBiometrics.SyncEmployeeBiometricsCommand(deviceId, employeeId));
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                $"Sync Biometrics for Employee {employeeId} from Device {deviceId}", result.Error);
        }
    }

    [JobDisplayName("Sync Logs with Cloud DB")]
    public async Task SyncCloudLogs()
    {
        var result = await _mediator.Send(new AttendanceSystem.Application.Features.CloudSync.Commands.SyncCloudLogs.SyncCloudLogsCommand());
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                "Sync Logs with Cloud DB", result.Error);
        }
    }

    /// <summary>
    /// Ya no se encola: la consolidación la hace AttendanceConsolidationWorker desde solicitudes durables.
    /// Se conserva para ejecutar los trabajos que quedaron en el almacenamiento de Hangfire antes del cambio.
    /// </summary>
    [JobDisplayName("Process Attendance for Employee {2} from {0:yyyy-MM-dd} to {1:yyyy-MM-dd}")]
    public async Task ProcessAttendance(DateTime startDate, DateTime endDate, string? employeeId)
    {
        var empId = string.IsNullOrEmpty(employeeId) ? null : AttendanceSystem.Domain.ValueObjects.EmployeeId.From(employeeId);
        try
        {
            await _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.ProcessDailyAttendanceCommand(startDate, endDate, EmployeeId: empId));
        }
        catch (Exception ex)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                $"Process Attendance for Employee {employeeId ?? "ALL"} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                ex.Message, ex);
        }
    }
}
