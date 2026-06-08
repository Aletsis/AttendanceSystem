using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using MediatR;

namespace AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;

public sealed record GetSystemConfigurationQuery : IRequest<Result<SystemConfigurationDto>>;

public sealed class GetSystemConfigurationQueryHandler : IRequestHandler<GetSystemConfigurationQuery, Result<SystemConfigurationDto>>
{
    private readonly ISystemConfigurationRepository _repository;

    public GetSystemConfigurationQueryHandler(ISystemConfigurationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<SystemConfigurationDto>> Handle(GetSystemConfigurationQuery request, CancellationToken cancellationToken)
    {
        var config = await _repository.GetConfigurationAsync(cancellationToken);

        if (config == null)
        {
            // Return default
            config = SystemConfiguration.CreateDefault();
        }

        return Result<SystemConfigurationDto>.Success(new SystemConfigurationDto(
            config.CompanyName,
            config.CompanyLogo,
            config.LateTolerance,
            config.StandardWorkHours,
            config.AutoClearDevicesAfterDownload,
            config.IsAutoDownloadEnabled,
            config.AutoDownloadTime,
            config.AutoDownloadOnlyToday,
            config.AdmsPort,
            config.BackupDirectory,
            config.BackupTimeoutMinutes,
            config.WorkPeriodMode,
            config.WeeklyStartDay,
            config.FortnightFirstDay,
            config.FortnightSecondDay,
            config.MonthlyStartDay,
            config.AreAlertsEnabled,
            config.AbsenceAlertEmails,
            config.LateAlertEmails,
            config.SystemFailureAlertEmails,
            config.SmtpHost,
            config.SmtpPort,
            config.SmtpUser,
            config.SmtpPassword,
            config.SmtpEnableSsl,
            config.IsAutoBackupEnabled,
            config.AutoBackupTime,
            config.IsAutoReportEnabled,
            config.AutoReportTime,
            config.AutoReportEmails,
            config.AutoReportForToday));
    }
}
