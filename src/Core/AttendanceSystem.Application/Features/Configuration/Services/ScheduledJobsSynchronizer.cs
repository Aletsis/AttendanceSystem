using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Repositories;

namespace AttendanceSystem.Application.Features.Configuration.Services;

/// <summary>
/// Programa en Hangfire las tareas que dependen de la configuración guardada: la descarga, el respaldo y el reporte
/// automáticos (horario o desactivadas) y las diarias de cálculo y cierre, todas en la zona horaria del sistema. Se aplica
/// al guardar la configuración, al arrancar y al reanudar las tareas tras una restauración, para que la programación
/// nunca quede distinta de lo guardado (antes sólo se aplicaba al guardar).
/// </summary>
public sealed class ScheduledJobsSynchronizer
{
    private readonly ISystemConfigurationRepository _configuration;
    private readonly IAttendanceJobScheduler _scheduler;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;

    public ScheduledJobsSynchronizer(
        ISystemConfigurationRepository configuration,
        IAttendanceJobScheduler scheduler,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null)
    {
        _configuration = configuration;
        _scheduler = scheduler;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default) =>
        await ApplyAsync(await _configuration.GetConfigurationAsync(cancellationToken), cancellationToken);

    /// <param name="config">Nula (instalación nueva): las automáticas quedan desactivadas.</param>
    public async Task ApplyAsync(SystemConfiguration? config, CancellationToken cancellationToken = default)
    {
        var timeZoneId = await _timeZoneResolver.GetSystemDefaultTimeZoneAsync(cancellationToken);

        _scheduler.ScheduleDailyCalculation(timeZoneId);

        if (config is { IsAutoDownloadEnabled: true, AutoDownloadTime: { } downloadTime })
            _scheduler.ScheduleAutoDownload(downloadTime, timeZoneId);
        else
            _scheduler.DisableAutoDownload();

        if (config is { IsAutoBackupEnabled: true, AutoBackupTime: { } backupTime })
            _scheduler.ScheduleAutoBackup(backupTime, timeZoneId);
        else
            _scheduler.DisableAutoBackup();

        if (config is { IsAutoReportEnabled: true, AutoReportTime: { } reportTime })
            _scheduler.ScheduleAutoReport(reportTime, timeZoneId);
        else
            _scheduler.DisableAutoReport();
    }
}
