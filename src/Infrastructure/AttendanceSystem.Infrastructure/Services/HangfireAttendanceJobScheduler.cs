using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using Hangfire;
using MediatR;
using AttendanceSystem.Application.Features.Configuration.Queries.GetSystemConfiguration;

namespace AttendanceSystem.Infrastructure.Services;

public class HangfireAttendanceJobScheduler : IAttendanceJobScheduler
{
    private const string JobId = "download-attendance-daily";
    private readonly IRecurringJobManager _recurringJobManager;

    public HangfireAttendanceJobScheduler(IRecurringJobManager recurringJobManager)
    {
        _recurringJobManager = recurringJobManager;
    }

    public void ScheduleAutoDownload(TimeSpan timeOfDay)
    {
        // Cron: Minute Hour * * *
        // Example: 14:30 -> "30 14 * * *"
        var cron = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * *";
        
        // Use Central Standard Time (CST) timezone
        // Hangfire by default uses UTC, so we need to specify the local timezone
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        
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
        var configResult = await _mediator.Send(new GetSystemConfigurationQuery());
        DateTime? fromDate = null;
        DateTime? toDate = null;

        if (configResult.IsSuccess && configResult.Value.AutoDownloadOnlyToday)
        {
             fromDate = DateTime.Today;
             toDate = DateTime.Today.AddDays(1).AddTicks(-1);
        }

        await _mediator.Send(new DownloadFromAllDevicesCommand(fromDate, toDate));
    }
}
