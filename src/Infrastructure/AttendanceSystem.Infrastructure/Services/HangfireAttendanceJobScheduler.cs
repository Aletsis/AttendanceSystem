using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using Hangfire;
using MediatR;

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
        
        _recurringJobManager.AddOrUpdate<AttendanceJobs>(
            JobId, 
            jobs => jobs.DownloadFromAllDevices(), 
            cron,
            timeZone);
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
        // Execute download for "yesterday" until "today" or just general sync?
        // Usually null means "smart sync" (last download to now).
        // Let's rely on the command's default behavior.
        await _mediator.Send(new DownloadFromAllDevicesCommand(null, null));
    }
}
