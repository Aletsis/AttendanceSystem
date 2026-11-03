using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;
using AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>Identificadores de las tareas recurrentes; también nombran su candado de ejecución única.</summary>
public static class AttendanceJobIds
{
    public const string AutoDownload = "download-attendance-daily";
    public const string AutoBackup = "automated-database-backup";
    public const string AutoReport = "automated-daily-report";
    public const string CriticalAbsences = "check-critical-absences";
    public const string DeviceHeartbeat = "device-heartbeat-monitor";
    public const string CloudSync = "cloud-log-sync";
    public const string ScheduleIntegrity = "schedule-integrity-check";
    public const string DailyCalculation = "daily-attendance-calculation";
    public const string AutoClosePeriods = "auto-close-periods";
}

public class HangfireAttendanceJobScheduler : IAttendanceJobScheduler
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireAttendanceJobScheduler(
        IRecurringJobManager recurringJobManager,
        IBackgroundJobClient backgroundJobClient)
    {
        _recurringJobManager = recurringJobManager;
        _backgroundJobClient = backgroundJobClient;
    }

    /// <summary>
    /// La zona del sistema (IANA, también en Windows); si el servidor no la conoce, la local del servidor, como antes.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)) return zone;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)) return zone;
        }
        return TimeZoneInfo.Local;
    }

    public void ScheduleAutoDownload(TimeSpan timeOfDay, string timeZoneId)
    {
        // Cron: Minute Hour * * *
        // Example: 14:30 -> "30 14 * * *"
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";

        // La hora configurada es la de la empresa (zona del sistema), no la del servidor.
        var options = new RecurringJobOptions
        {
            TimeZone = ResolveTimeZone(timeZoneId)
        };

        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.AutoDownload,
            jobs => jobs.DownloadFromAllDevices(CancellationToken.None),
            cron,
            options);
    }

    public void DisableAutoDownload()
    {
        _recurringJobManager.RemoveIfExists(AttendanceJobIds.AutoDownload);
    }

    public void ScheduleCriticalAbsenceCheck()
    {
        // Every 15 minutes: "*/15 * * * *"
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.CriticalAbsences,
            jobs => jobs.CheckCriticalAbsences(CancellationToken.None),
            "*/15 * * * *");
    }
    public void ScheduleAutoBackup(TimeSpan timeOfDay, string timeZoneId)
    {
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";
        var timeZone = ResolveTimeZone(timeZoneId);

        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.AutoBackup,
            jobs => jobs.PerformAutoBackup(CancellationToken.None),
            cron,
            new RecurringJobOptions { TimeZone = timeZone });
    }

    public void DisableAutoBackup()
    {
        _recurringJobManager.RemoveIfExists(AttendanceJobIds.AutoBackup);
    }

    public void ScheduleAutoReport(TimeSpan timeOfDay, string timeZoneId)
    {
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";
        var timeZone = ResolveTimeZone(timeZoneId);

        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.AutoReport,
            jobs => jobs.SendAutoReport(CancellationToken.None),
            cron,
            new RecurringJobOptions { TimeZone = timeZone });
    }

    public void DisableAutoReport()
    {
        _recurringJobManager.RemoveIfExists(AttendanceJobIds.AutoReport);
    }
    public void ScheduleDeviceHeartbeat()
    {
        // Every 10 minutes
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.DeviceHeartbeat,
            jobs => jobs.MonitorDeviceHealth(CancellationToken.None),
            "*/10 * * * *");
    }

    public void ScheduleCloudSync()
    {
        // Every 5 minutes
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.CloudSync,
            jobs => jobs.SyncCloudLogs(CancellationToken.None),
            "*/5 * * * *");
    }

    public void ScheduleScheduleIntegrityCheck()
    {
        // Every hour, and once now so a fresh start reports at once
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.ScheduleIntegrity,
            jobs => jobs.CheckScheduleIntegrity(CancellationToken.None),
            "0 * * * *");
        _backgroundJobClient.Enqueue<AttendanceJobs>(jobs => jobs.CheckScheduleIntegrity(CancellationToken.None));
    }

    public void ScheduleDailyCalculation(string timeZoneId)
    {
        var timeZone = ResolveTimeZone(timeZoneId);

        // 00:05 en la zona del sistema: el día anterior ya terminó.
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.DailyCalculation,
            jobs => jobs.CalculatePreviousDay(CancellationToken.None),
            "5 0 * * *",
            new RecurringJobOptions { TimeZone = timeZone });

        // Caso 17: 00:30, cierra los periodos que terminaron hace más de 3 días.
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.AutoClosePeriods,
            jobs => jobs.AutoClosePeriods(CancellationToken.None),
            "30 0 * * *",
            new RecurringJobOptions { TimeZone = timeZone });
    }

    public void EnqueueBiometricSync(string deviceId, IReadOnlyCollection<string> employeeIds)
    {
        var ids = employeeIds.ToArray();
        _backgroundJobClient.Enqueue<AttendanceJobs>(
            jobs => jobs.SyncDeviceBiometrics(deviceId, ids, CancellationToken.None));
    }
}

/// <summary>
/// Tareas de Hangfire. Política común:
/// <list type="bullet">
/// <item>Sin reintentos automáticos: los de Hangfire (10, con esperas de horas o días) acumulaban ejecuciones de las
/// tareas frecuentes y corrían las diarias a deshoras. Una periódica la cubre su siguiente pasada; una diaria fallida
/// queda en "Failed" en el panel. Las frecuentes fallidas se borran (el error queda en el log) para no llenar el
/// panel con cientos de fallos al día mientras dura una caída.</item>
/// <item>Una sola ejecución a la vez de cada tarea (<see cref="JobExclusionLock"/>): si la anterior sigue corriendo, la
/// nueva se omite.</item>
/// <item>Cancelables: Hangfire pasa el token de apagado, que se cancela al detener el servidor o pausarlo para una
/// restauración.</item>
/// </list>
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class AttendanceJobs
{
    private readonly IMediator _mediator;
    private readonly JobExclusionLock? _exclusion;
    private readonly ILogger<AttendanceJobs> _logger;

    public AttendanceJobs(IMediator mediator, JobExclusionLock? exclusion = null, ILogger<AttendanceJobs>? logger = null)
    {
        _mediator = mediator;
        _exclusion = exclusion;
        _logger = logger ?? NullLogger<AttendanceJobs>.Instance;
    }

    /// <summary>Ejecuta la tarea si no hay otra ejecución de la misma en curso; si la hay, la omite.</summary>
    private async Task RunExclusiveAsync(string jobId, CancellationToken cancellationToken, Func<Task> body)
    {
        if (_exclusion == null)
        {
            await body();
            return;
        }

        await using var held = await _exclusion.TryAcquireAsync(jobId, cancellationToken);
        if (held == null)
        {
            _logger.LogInformation("La tarea {JobId} sigue en ejecución desde la pasada anterior; se omite esta.", jobId);
            return;
        }
        await body();
    }

    [JobDisplayName("Download Logs from All Devices")]
    public Task DownloadFromAllDevices(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.AutoDownload, cancellationToken, async () =>
        {
            var result = await _mediator.Send(new DownloadFromAllDevicesCommand(null, null), cancellationToken);
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
        });

    [JobDisplayName("Calculate Previous Day Attendance")]
    public Task CalculatePreviousDay(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.DailyCalculation, cancellationToken, () =>
            _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Consolidation.CalculatePreviousDayCommand(), cancellationToken));

    [JobDisplayName("Auto-close Attendance Periods")]
    public Task AutoClosePeriods(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.AutoClosePeriods, cancellationToken, () =>
            _mediator.Send(new AttendanceSystem.Application.Features.Attendance.PeriodClosure.AutoClosePeriodsCommand(), cancellationToken));

    [JobDisplayName("Check for Critical Position Absences")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task CheckCriticalAbsences(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.CriticalAbsences, cancellationToken, async () =>
        {
            var result = await _mediator.Send(new CheckCriticalAbsencesCommand(), cancellationToken);
            if (!result.IsSuccess)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Check for Critical Position Absences", result.Error);
            }
        });

    [JobDisplayName("Extend Rotation Rosters and Check Schedules")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task CheckScheduleIntegrity(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.ScheduleIntegrity, cancellationToken, async () =>
        {
            await _mediator.Send(new AttendanceSystem.Application.Features.Roster.Commands.ExtendRotationPatterns.ExtendRotationPatternsCommand(), cancellationToken);
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.Shifts.Commands.CheckScheduleIntegrity.CheckScheduleIntegrityCommand(), cancellationToken);
            if (!result.IsSuccess)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Extend Rotation Rosters and Check Schedules", result.Error);
            }
        });

    [JobDisplayName("Automated Database Backup")]
    public Task PerformAutoBackup(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.AutoBackup, cancellationToken, async () =>
        {
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.Backup.Commands.CreateBackupCommand("Full", "Respaldo Automático Programado"), cancellationToken);
            if (!result.Success)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Automated Database Backup", result.Message);
            }
        });

    [JobDisplayName("Automated Daily Report")]
    public Task SendAutoReport(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.AutoReport, cancellationToken, async () =>
        {
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport.SendDailyReportCommand(), cancellationToken);
            if (!result.IsSuccess)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Automated Daily Report", result.Error);
            }
        });

    [JobDisplayName("Monitor Device Health (Heartbeat)")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task MonitorDeviceHealth(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.DeviceHeartbeat, cancellationToken, async () =>
        {
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.Devices.Commands.MonitorDeviceHealth.MonitorDeviceHealthCommand(), cancellationToken);
            if (!result.IsSuccess)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Monitor Device Health (Heartbeat)", result.Error);
            }
        });

    [JobDisplayName("Sync Missing Biometrics from Device {0}")]
    public async Task SyncDeviceBiometrics(string deviceId, string[] employeeIds, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AttendanceSystem.Application.Features.Employees.Commands.SyncEmployeeBiometrics.SyncEmployeeBiometricsCommand(deviceId, employeeIds), cancellationToken);
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                $"Sync Missing Biometrics from Device {deviceId}", result.Error);
        }
    }

    [JobDisplayName("Sync Logs with Cloud DB")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task SyncCloudLogs(CancellationToken cancellationToken) =>
        RunExclusiveAsync(AttendanceJobIds.CloudSync, cancellationToken, async () =>
        {
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.CloudSync.Commands.SyncCloudLogs.SyncCloudLogsCommand(), cancellationToken);
            if (!result.IsSuccess)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Sync Logs with Cloud DB", result.Error);
            }
        });

    // Firmas anteriores: las tareas de horario configurable (descarga, respaldo, reporte) quedan guardadas en Hangfire sin
    // token hasta que se vuelvan a programar, y los biométricos encolados antes, uno por empleado.

    [JobDisplayName("Download Logs from All Devices")]
    public Task DownloadFromAllDevices() => DownloadFromAllDevices(CancellationToken.None);

    [JobDisplayName("Automated Database Backup")]
    public Task PerformAutoBackup() => PerformAutoBackup(CancellationToken.None);

    [JobDisplayName("Automated Daily Report")]
    public Task SendAutoReport() => SendAutoReport(CancellationToken.None);

    [JobDisplayName("Sync Biometrics for Employee {1} from Device {0}")]
    public Task SyncEmployeeBiometrics(string deviceId, string employeeId) =>
        SyncDeviceBiometrics(deviceId, new[] { employeeId }, CancellationToken.None);

    /// <summary>
    /// Ya no se encola: la consolidación la hace AttendanceConsolidationWorker desde solicitudes durables.
    /// Se conserva para ejecutar los trabajos que quedaron en el almacenamiento de Hangfire antes del cambio.
    /// </summary>
    [JobDisplayName("Process Attendance for Employee {2} from {0:yyyy-MM-dd} to {1:yyyy-MM-dd}")]
    [AutomaticRetry(Attempts = 10)]
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
