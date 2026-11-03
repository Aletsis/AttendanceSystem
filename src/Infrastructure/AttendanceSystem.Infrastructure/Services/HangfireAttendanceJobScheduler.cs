using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using Hangfire;
using Hangfire.Server;
using AttendanceSystem.Application.Features.Automation;
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
            jobs => jobs.DownloadFromAllDevices(null, CancellationToken.None),
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
            jobs => jobs.PerformAutoBackup(null, CancellationToken.None),
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
            jobs => jobs.SendAutoReport(null, CancellationToken.None),
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
            jobs => jobs.CalculatePreviousDay(null, CancellationToken.None),
            "5 0 * * *",
            new RecurringJobOptions { TimeZone = timeZone });

        // Caso 17: 00:30, cierra los periodos que terminaron hace más de 3 días.
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            AttendanceJobIds.AutoClosePeriods,
            jobs => jobs.AutoClosePeriods(null, CancellationToken.None),
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
/// <item>Sin los reintentos por defecto de Hangfire (10, con esperas de horas o días), que acumulaban ejecuciones de las
/// tareas frecuentes y corrían las diarias a deshoras. Una periódica la cubre su siguiente pasada; sus fallos se borran
/// del panel (el error queda en el log) para no llenarlo con cientos al día mientras dura una caída.</item>
/// <item>Respaldo y reporte: 2 reintentos dentro de la hora siguiente (a los 10 y a los 30 minutos).</item>
/// <item>Descarga diaria: reintenta sólo los relojes que fallaron, 2 veces (a los 10 y a los 30 minutos); la tarea
/// queda correcta y, si siguen fallando, deja una alerta con esos relojes.</item>
/// <item>Las diarias que fallan del todo (agotados sus reintentos) quedan en "Failed" y dejan una alerta
/// (<see cref="OperationalAlerts"/>), que se resuelve cuando la tarea vuelve a terminar bien.</item>
/// <item>Fecha de negocio fija: la del momento en que se disparó la tarea, en la zona del sistema; un reintento después
/// de medianoche no cambia de día.</item>
/// <item>Una sola ejecución a la vez de cada tarea (<see cref="JobExclusionLock"/>): si la anterior sigue corriendo, la
/// nueva se omite.</item>
/// <item>Cancelables: Hangfire pasa el token de apagado, que se cancela al detener el servidor o pausarlo para una
/// restauración.</item>
/// </list>
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class AttendanceJobs
{
    /// <summary>Reintentos de respaldo, reporte y relojes caídos; deben coincidir con los atributos de esas tareas.</summary>
    public const int DailyRetries = 2;
    /// <summary>Espera antes de cada reintento (10 y 30 minutos: los dos caen dentro de la hora siguiente).</summary>
    public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30) };

    public const string DownloadRetryJobName = "Reintento de relojes caídos";

    private readonly IMediator _mediator;
    private readonly JobExclusionLock? _exclusion;
    private readonly ILogger<AttendanceJobs> _logger;
    private readonly OperationalAlerts? _alerts;
    private readonly IBackgroundJobClient? _backgroundJobs;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ITimeNormalizer _timeNormalizer;

    public AttendanceJobs(
        IMediator mediator,
        JobExclusionLock? exclusion = null,
        ILogger<AttendanceJobs>? logger = null,
        OperationalAlerts? alerts = null,
        IBackgroundJobClient? backgroundJobs = null,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        ITimeNormalizer? timeNormalizer = null)
    {
        _mediator = mediator;
        _exclusion = exclusion;
        _logger = logger ?? NullLogger<AttendanceJobs>.Instance;
        _alerts = alerts;
        _backgroundJobs = backgroundJobs;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _timeNormalizer = timeNormalizer ?? DefaultTimeNormalizer.Instance;
    }

    /// <summary>Cuándo se disparó la tarea (igual en todos sus reintentos) y cuántos reintentos lleva.</summary>
    internal readonly record struct JobRun(DateTime TriggeredAtUtc, int RetryCount);

    private static JobRun RunOf(PerformContext? context) =>
        new(context?.BackgroundJob.CreatedAt ?? DateTime.UtcNow, context?.GetJobParameter<int>("RetryCount") ?? 0);

    public static string FailureAlertPrefix(string jobId) => $"tarea:{jobId}:";
    public const string FailedDevicesAlertPrefix = "relojes:";

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

    /// <summary>
    /// Tarea diaria: corre con su fecha de negocio; si falla en su último intento deja una alerta, y si termina bien
    /// resuelve las de sus fallos anteriores.
    /// </summary>
    private Task RunDailyAsync(string jobId, string jobName, JobRun run, int retries, CancellationToken cancellationToken, Func<DateTime, Task> body) =>
        RunExclusiveAsync(jobId, cancellationToken, async () =>
        {
            var today = await BusinessDateAsync(run, cancellationToken);
            try
            {
                await body(today);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && run.RetryCount >= retries)
            {
                var afterRetries = retries > 0 ? $" después de {retries} reintentos" : "";
                await TryRaiseAsync($"{FailureAlertPrefix(jobId)}{today:yyyyMMdd}", $"Falló la tarea automática: {jobName}",
                    $"La tarea automática «{jobName}» del {today:dd/MM/yyyy} falló{afterRetries}: {ex.Message}", cancellationToken);
                throw;
            }
            await TryResolveAsync(FailureAlertPrefix(jobId), cancellationToken);
        });

    private async Task<DateTime> BusinessDateAsync(JobRun run, CancellationToken cancellationToken)
    {
        var timeZoneId = await _timeZoneResolver.GetSystemDefaultTimeZoneAsync(cancellationToken);
        return _timeNormalizer.ToCivilTime(run.TriggeredAtUtc, timeZoneId).Date;
    }

    // La alerta acompaña al fallo; si no se puede registrar, el fallo original sigue su curso.
    private async Task TryRaiseAsync(string key, string subject, string message, CancellationToken cancellationToken)
    {
        if (_alerts == null)
        {
            _logger.LogWarning("ALERTA {Key}: {Message}", key, message);
            return;
        }
        try
        {
            await _alerts.RaiseAsync(key, subject, message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "No se pudo registrar la alerta {Key}: {Message}", key, message);
        }
    }

    private async Task TryResolveAsync(string keyPrefix, CancellationToken cancellationToken)
    {
        if (_alerts == null) return;
        try
        {
            await _alerts.ResolveAsync(keyPrefix, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudieron resolver las alertas {Prefix}*.", keyPrefix);
        }
    }

    [JobDisplayName("Download Logs from All Devices")]
    public Task DownloadFromAllDevices(PerformContext? context, CancellationToken cancellationToken) =>
        DownloadFromAllDevicesAsync(RunOf(context), cancellationToken);

    internal Task DownloadFromAllDevicesAsync(JobRun run, CancellationToken cancellationToken) =>
        RunDailyAsync(AttendanceJobIds.AutoDownload, "Descarga automática de relojes", run, 0, cancellationToken, async today =>
        {
            var failed = await DownloadAsync(null, cancellationToken);
            if (failed.Count == 0)
            {
                await TryResolveAsync(FailedDevicesAlertPrefix, cancellationToken);
                return;
            }

            // Los relojes que respondieron ya quedaron descargados: sólo se reintentan los demás, y la tarea queda correcta.
            _logger.LogWarning("Descarga automática: {Count} relojes fallaron; se reintentarán. {Errors}", failed.Count, Describe(failed));
            await ScheduleDeviceRetryAsync(failed, 1, today, cancellationToken);
        });

    [JobDisplayName("Retry Download from Failed Devices (attempt {1})")]
    public Task RetryDeviceDownloads(string[] deviceIds, int attempt, DateTime businessDate, CancellationToken cancellationToken) =>
        RetryDeviceDownloadsAsync(deviceIds, attempt, businessDate, cancellationToken);

    internal async Task RetryDeviceDownloadsAsync(string[] deviceIds, int attempt, DateTime businessDate, CancellationToken cancellationToken)
    {
        IReadOnlyList<(string DeviceId, string? Error)> failed;
        try
        {
            failed = await DownloadAsync(deviceIds, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed = deviceIds.Select(id => (id, (string?)ex.Message)).ToList();
        }

        if (failed.Count == 0)
        {
            _logger.LogInformation("Reintento {Attempt} de la descarga automática: los relojes {Devices} ya se descargaron.", attempt, string.Join(", ", deviceIds));
            return;
        }

        if (attempt < DailyRetries)
        {
            await ScheduleDeviceRetryAsync(failed, attempt + 1, businessDate, cancellationToken);
            return;
        }

        await TryRaiseAsync($"{FailedDevicesAlertPrefix}{businessDate:yyyyMMdd}", "Relojes sin descargar",
            $"La descarga automática del {businessDate:dd/MM/yyyy} no pudo leer {failed.Count} reloj(es) después de {DailyRetries} reintentos: " +
            $"{Describe(failed)}. Revise su conexión; sus marcajes se descargarán en la siguiente descarga automática o manual.", cancellationToken);
    }

    /// <summary>Descarga (todos o los indicados) y devuelve los relojes que fallaron; un fallo general es una excepción.</summary>
    private async Task<IReadOnlyList<(string DeviceId, string? Error)>> DownloadAsync(IReadOnlyCollection<string>? deviceIds, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DownloadFromAllDevicesCommand(null, null, DeviceIds: deviceIds), cancellationToken);
        if (!result.IsSuccess)
        {
            throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException("Download Logs from All Devices", result.Error);
        }
        return result.Value.Where(d => !d.Success).Select(d => (d.DeviceId, d.ErrorMessage)).ToList();
    }

    private async Task ScheduleDeviceRetryAsync(IReadOnlyList<(string DeviceId, string? Error)> failed, int attempt, DateTime businessDate, CancellationToken cancellationToken)
    {
        var ids = failed.Select(f => f.DeviceId).ToArray();
        if (_backgroundJobs == null)
        {
            await TryRaiseAsync($"{FailedDevicesAlertPrefix}{businessDate:yyyyMMdd}", "Relojes sin descargar",
                $"La descarga automática del {businessDate:dd/MM/yyyy} no pudo leer {failed.Count} reloj(es): {Describe(failed)}.", cancellationToken);
            return;
        }
        _backgroundJobs.Schedule<AttendanceJobs>(
            jobs => jobs.RetryDeviceDownloads(ids, attempt, businessDate, CancellationToken.None),
            RetryDelays[attempt - 1]);
    }

    private static string Describe(IEnumerable<(string DeviceId, string? Error)> failed) =>
        string.Join("; ", failed.Select(f => $"{f.DeviceId}: {f.Error ?? "error desconocido"}"));

    [JobDisplayName("Calculate Previous Day Attendance")]
    public Task CalculatePreviousDay(PerformContext? context, CancellationToken cancellationToken) =>
        RunDailyAsync(AttendanceJobIds.DailyCalculation, "Cálculo del día anterior", RunOf(context), 0, cancellationToken, today =>
            _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Consolidation.CalculatePreviousDayCommand(today), cancellationToken));

    [JobDisplayName("Auto-close Attendance Periods")]
    public Task AutoClosePeriods(PerformContext? context, CancellationToken cancellationToken) =>
        RunDailyAsync(AttendanceJobIds.AutoClosePeriods, "Cierre automático de periodos", RunOf(context), 0, cancellationToken, today =>
            _mediator.Send(new AttendanceSystem.Application.Features.Attendance.PeriodClosure.AutoClosePeriodsCommand(today), cancellationToken));

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
    [AutomaticRetry(Attempts = DailyRetries, DelaysInSeconds = new[] { 600, 1800 })]
    public Task PerformAutoBackup(PerformContext? context, CancellationToken cancellationToken) =>
        PerformAutoBackupAsync(RunOf(context), cancellationToken);

    internal Task PerformAutoBackupAsync(JobRun run, CancellationToken cancellationToken) =>
        RunDailyAsync(AttendanceJobIds.AutoBackup, "Respaldo automático", run, DailyRetries, cancellationToken, async _ =>
        {
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.Backup.Commands.CreateBackupCommand("Full", "Respaldo Automático Programado"), cancellationToken);
            if (!result.Success)
            {
                throw new AttendanceSystem.Infrastructure.Exceptions.JobExecutionFailedException(
                    "Automated Database Backup", result.Message);
            }
        });

    [JobDisplayName("Automated Daily Report")]
    [AutomaticRetry(Attempts = DailyRetries, DelaysInSeconds = new[] { 600, 1800 })]
    public Task SendAutoReport(PerformContext? context, CancellationToken cancellationToken) =>
        SendAutoReportAsync(RunOf(context), cancellationToken);

    internal Task SendAutoReportAsync(JobRun run, CancellationToken cancellationToken) =>
        RunDailyAsync(AttendanceJobIds.AutoReport, "Reporte diario automático", run, DailyRetries, cancellationToken, async today =>
        {
            var result = await _mediator.Send(new AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport.SendDailyReportCommand(today), cancellationToken);
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

    // Firmas anteriores de trabajos que pueden seguir en la cola de Hangfire: una ejecución de descarga, respaldo o
    // reporte ya encolada antes de actualizar, y los biométricos encolados uno por empleado.

    [JobDisplayName("Download Logs from All Devices")]
    public Task DownloadFromAllDevices() => DownloadFromAllDevices(null, CancellationToken.None);

    [JobDisplayName("Automated Database Backup")]
    [AutomaticRetry(Attempts = DailyRetries, DelaysInSeconds = new[] { 600, 1800 })]
    public Task PerformAutoBackup() => PerformAutoBackup(null, CancellationToken.None);

    [JobDisplayName("Automated Daily Report")]
    [AutomaticRetry(Attempts = DailyRetries, DelaysInSeconds = new[] { 600, 1800 })]
    public Task SendAutoReport() => SendAutoReport(null, CancellationToken.None);

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
