using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using MediatR;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Configuration.Commands.UpdateSystemConfiguration;

public sealed record UpdateSystemConfigurationCommand(
    string CompanyName,
    byte[]? CompanyLogo,
    TimeSpan LateTolerance,
    TimeSpan StandardWorkHours,
    bool AutoClearDevicesAfterDownload,
    bool IsAutoDownloadEnabled,
    TimeSpan? AutoDownloadTime,
    bool AutoDownloadOnlyToday = false,
    int AdmsPort = 16373,
    string BackupDirectory = "Backups",
    int BackupTimeoutMinutes = 10,
    WorkPeriodMode WorkPeriodMode = WorkPeriodMode.Weekly,
    DayOfWeek WeeklyStartDay = DayOfWeek.Monday,
    int FortnightFirstDay = 1,
    int FortnightSecondDay = 16,
    int MonthlyStartDay = 1,
    bool AreAlertsEnabled = false,
    string? AbsenceAlertEmails = null,
    string? LateAlertEmails = null,
    string? SystemFailureAlertEmails = null,
    string? SmtpHost = null,
    int SmtpPort = 587,
    string? SmtpUser = null,
    string? SmtpPassword = null,
    bool SmtpEnableSsl = true,
    bool IsAutoBackupEnabled = false,
    TimeSpan? AutoBackupTime = null,
    bool IsAutoReportEnabled = false,
    TimeSpan? AutoReportTime = null,
    string? AutoReportEmails = null,
    bool AutoReportForToday = false) : IRequest<Result<Guid>>;

public sealed class UpdateSystemConfigurationCommandHandler : IRequestHandler<UpdateSystemConfigurationCommand, Result<Guid>>
{
    private readonly ISystemConfigurationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceJobScheduler _jobScheduler;

    public UpdateSystemConfigurationCommandHandler(
        ISystemConfigurationRepository repository,
        IUnitOfWork unitOfWork,
        IAttendanceJobScheduler jobScheduler)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _jobScheduler = jobScheduler;
    }

    public async Task<Result<Guid>> Handle(UpdateSystemConfigurationCommand command, CancellationToken cancellationToken)
    {
        var config = await _repository.GetConfigurationAsync(cancellationToken);

        if (config == null)
        {
            config = SystemConfiguration.CreateDefault();
            _repository.Add(config);
        }

        config.UpdateSettings(
            command.CompanyName,
            command.CompanyLogo,
            command.LateTolerance,
            command.StandardWorkHours,
            command.AutoClearDevicesAfterDownload,
            command.IsAutoDownloadEnabled,
            command.AutoDownloadTime,
            command.AutoDownloadOnlyToday,
            command.AdmsPort,
            command.BackupDirectory,
            command.BackupTimeoutMinutes,
            command.AreAlertsEnabled,
            command.AbsenceAlertEmails,
            command.LateAlertEmails,
            command.SystemFailureAlertEmails,
            command.SmtpHost,
            command.SmtpPort,
            command.SmtpUser,
            command.SmtpPassword,
            command.SmtpEnableSsl,
            command.IsAutoBackupEnabled,
            command.AutoBackupTime,
            command.IsAutoReportEnabled,
            command.AutoReportTime,
            command.AutoReportEmails,
            command.AutoReportForToday);

        config.UpdateWorkPeriodSettings(
            command.WorkPeriodMode,
            command.WeeklyStartDay,
            command.FortnightFirstDay,
            command.FortnightSecondDay,
            command.MonthlyStartDay);

        // Update Jobs
        if (config.IsAutoDownloadEnabled && config.AutoDownloadTime.HasValue)
        {
            _jobScheduler.ScheduleAutoDownload(config.AutoDownloadTime.Value);
        }
        else
        {
            _jobScheduler.DisableAutoDownload();
        }

        if (config.IsAutoBackupEnabled && config.AutoBackupTime.HasValue)
        {
            _jobScheduler.ScheduleAutoBackup(config.AutoBackupTime.Value);
        }
        else
        {
            _jobScheduler.DisableAutoBackup();
        }

        if (config.IsAutoReportEnabled && config.AutoReportTime.HasValue)
        {
            _jobScheduler.ScheduleAutoReport(config.AutoReportTime.Value);
        }
        else
        {
            _jobScheduler.DisableAutoReport();
        }

        // Add call to repository Update if tracking is not automatic 
        if (await _repository.GetConfigurationAsync(cancellationToken) != null) 
        {
             _repository.Update(config);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(config.Id);
    }
}
