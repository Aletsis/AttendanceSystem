using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Configuration.Services;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Repositories;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Configuration;

/// <summary>Auditoría de tareas, bloque E: la programación sale siempre de la configuración guardada y de la zona del sistema.</summary>
public class ScheduledJobsSynchronizerTests
{
    private readonly Mock<IAttendanceJobScheduler> _scheduler = new();
    private readonly Mock<IAuthoritativeTimeZoneResolver> _zones = new();

    public ScheduledJobsSynchronizerTests() =>
        _zones.Setup(z => z.GetSystemDefaultTimeZoneAsync(It.IsAny<CancellationToken>())).ReturnsAsync("America/Tijuana");

    private ScheduledJobsSynchronizer Synchronizer(SystemConfiguration? saved = null)
    {
        var repository = new Mock<ISystemConfigurationRepository>();
        repository.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        return new ScheduledJobsSynchronizer(repository.Object, _scheduler.Object, _zones.Object);
    }

    private static SystemConfiguration Configured(bool download, bool backup, bool report)
    {
        var config = SystemConfiguration.CreateDefault();
        config.UpdateSettings("Empresa", null, TimeSpan.FromMinutes(10), TimeSpan.FromHours(8), false,
            download, new TimeSpan(23, 0, 0), false, 18373, "/respaldos", 10, false, null, null, null, null, 587, null, null, true,
            backup, new TimeSpan(2, 0, 0), report, new TimeSpan(6, 0, 0), "rh@empresa.com", false);
        return config;
    }

    [Fact]
    public async Task EnabledJobs_AreScheduledAtTheirTime_InTheSystemTimeZone()
    {
        await Synchronizer(Configured(true, true, true)).ApplyAsync();

        _scheduler.Verify(s => s.ScheduleAutoDownload(new TimeSpan(23, 0, 0), "America/Tijuana"), Times.Once);
        _scheduler.Verify(s => s.ScheduleAutoBackup(new TimeSpan(2, 0, 0), "America/Tijuana"), Times.Once);
        _scheduler.Verify(s => s.ScheduleAutoReport(new TimeSpan(6, 0, 0), "America/Tijuana"), Times.Once);
        _scheduler.Verify(s => s.ScheduleDailyCalculation("America/Tijuana"), Times.Once);
    }

    [Fact]
    public async Task DisabledJobs_AreRemoved()
    {
        await Synchronizer(Configured(false, true, false)).ApplyAsync();

        _scheduler.Verify(s => s.DisableAutoDownload(), Times.Once);
        _scheduler.Verify(s => s.ScheduleAutoBackup(It.IsAny<TimeSpan>(), It.IsAny<string>()), Times.Once);
        _scheduler.Verify(s => s.DisableAutoReport(), Times.Once);
        _scheduler.Verify(s => s.ScheduleAutoDownload(It.IsAny<TimeSpan>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WithoutSavedConfiguration_OnlyTheDailyJobsRemain()
    {
        await Synchronizer(null).ApplyAsync();

        _scheduler.Verify(s => s.ScheduleDailyCalculation("America/Tijuana"), Times.Once);
        _scheduler.Verify(s => s.DisableAutoDownload(), Times.Once);
        _scheduler.Verify(s => s.DisableAutoBackup(), Times.Once);
        _scheduler.Verify(s => s.DisableAutoReport(), Times.Once);
    }
}
