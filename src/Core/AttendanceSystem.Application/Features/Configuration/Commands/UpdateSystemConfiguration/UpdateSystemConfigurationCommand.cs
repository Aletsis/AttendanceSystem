using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using MediatR;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Configuration.Commands.UpdateSystemConfiguration;

public sealed record UpdateSystemConfigurationCommand(
    TimeSpan LateTolerance,
    TimeSpan StandardWorkHours,
    bool AutoClearDevicesAfterDownload,
    bool SendEmailAlerts,
    string? AlertEmailRecipient,
    bool IsAutoDownloadEnabled,
    TimeSpan? AutoDownloadTime,
    WorkPeriodMode WorkPeriodMode = WorkPeriodMode.Weekly,
    DayOfWeek WeeklyStartDay = DayOfWeek.Monday,
    int FortnightFirstDay = 1,
    int FortnightSecondDay = 16,
    int MonthlyStartDay = 1) : IRequest<Result<Guid>>;

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
            command.LateTolerance,
            command.StandardWorkHours,
            command.AutoClearDevicesAfterDownload,
            command.IsAutoDownloadEnabled,
            command.AutoDownloadTime);

        config.UpdateWorkPeriodSettings(
            command.WorkPeriodMode,
            command.WeeklyStartDay,
            command.FortnightFirstDay,
            command.FortnightSecondDay,
            command.MonthlyStartDay);

        // Update Job
        if (config.IsAutoDownloadEnabled && config.AutoDownloadTime.HasValue)
        {
            _jobScheduler.ScheduleAutoDownload(config.AutoDownloadTime.Value);
        }
        else
        {
            _jobScheduler.DisableAutoDownload();
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
