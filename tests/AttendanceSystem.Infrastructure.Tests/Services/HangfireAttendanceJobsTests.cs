using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;
using AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;
using AttendanceSystem.Application.Features.CloudSync.Commands.SyncCloudLogs;
using AttendanceSystem.Infrastructure.Exceptions;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class HangfireAttendanceJobsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly AttendanceJobs _jobs;

    public HangfireAttendanceJobsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _jobs = new AttendanceJobs(_mediatorMock.Object);
    }

    [Fact]
    public async Task CheckCriticalAbsences_WhenFails_ShouldThrowJobExecutionFailedException()
    {
        // Arrange
        _mediatorMock.Setup(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure("Error simulado de ausencias críticas"));

        // Act
        var act = () => _jobs.CheckCriticalAbsences(CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<JobExecutionFailedException>();
        ex.Which.JobName.Should().Be("Check for Critical Position Absences");
        ex.Which.Message.Should().Contain("Error simulado de ausencias críticas");
    }

    [Fact]
    public async Task SyncCloudLogs_WhenFails_ShouldThrowJobExecutionFailedException()
    {
        // Arrange
        _mediatorMock.Setup(m => m.Send(It.IsAny<SyncCloudLogsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SyncCloudLogsResultDto>.Failure("Error en sincronización en la nube"));

        // Act
        var act = () => _jobs.SyncCloudLogs(CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<JobExecutionFailedException>();
        ex.Which.JobName.Should().Be("Sync Logs with Cloud DB");
        ex.Which.Message.Should().Contain("Error en sincronización en la nube");
    }

    [Fact]
    public async Task ProcessAttendance_WhenExceptionThrown_ShouldWrapInJobExecutionFailedException()
    {
        // Arrange
        _mediatorMock.Setup(m => m.Send(It.IsAny<AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.ProcessDailyAttendanceCommand>(), default))
            .ThrowsAsync(new InvalidOperationException("Falla de cálculo"));

        // Act
        var act = () => _jobs.ProcessAttendance(DateTime.Today, DateTime.Today, "EMP-100");

        // Assert
        var ex = await act.Should().ThrowAsync<JobExecutionFailedException>();
        ex.Which.JobName.Should().Contain("EMP-100");
        ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    private static AttendanceDbContext InMemoryContext() =>
        new(new DbContextOptionsBuilder<AttendanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Mock<IPublisher>().Object);

    [Fact]
    public async Task ARunThatOverlapsThePreviousOne_IsSkipped()
    {
        var release = new TaskCompletionSource();
        var calls = 0;
        _mediatorMock.Setup(m => m.Send(It.IsAny<SyncCloudLogsCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref calls);
                await release.Task;
                return Result<SyncCloudLogsResultDto>.Success(new SyncCloudLogsResultDto(0, 0));
            });
        var first = new AttendanceJobs(_mediatorMock.Object, new JobExclusionLock(InMemoryContext()));
        var second = new AttendanceJobs(_mediatorMock.Object, new JobExclusionLock(InMemoryContext()));

        var running = first.SyncCloudLogs(CancellationToken.None);
        await second.SyncCloudLogs(CancellationToken.None);
        calls.Should().Be(1);

        release.SetResult();
        await running;
        await second.SyncCloudLogs(CancellationToken.None);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task DifferentJobs_DoNotExcludeEachOther()
    {
        var release = new TaskCompletionSource();
        _mediatorMock.Setup(m => m.Send(It.IsAny<SyncCloudLogsCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return Result<SyncCloudLogsResultDto>.Success(new SyncCloudLogsResultDto(0, 0));
            });
        _mediatorMock.Setup(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));
        var jobs = new AttendanceJobs(_mediatorMock.Object, new JobExclusionLock(InMemoryContext()));

        var running = jobs.SyncCloudLogs(CancellationToken.None);
        await jobs.CheckCriticalAbsences(CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        release.SetResult();
        await running;
    }

    [Fact]
    public async Task AFailedRun_ReleasesTheLock()
    {
        _mediatorMock.Setup(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure("falla"));
        var jobs = new AttendanceJobs(_mediatorMock.Object, new JobExclusionLock(InMemoryContext()));

        await jobs.Invoking(j => j.CheckCriticalAbsences(CancellationToken.None)).Should().ThrowAsync<JobExecutionFailedException>();
        await jobs.Invoking(j => j.CheckCriticalAbsences(CancellationToken.None)).Should().ThrowAsync<JobExecutionFailedException>();

        _mediatorMock.Verify(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TheShutdownToken_ReachesTheCommand()
    {
        using var shutdown = new CancellationTokenSource();
        _mediatorMock.Setup(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), shutdown.Token))
            .ReturnsAsync(Result<int>.Success(0));

        await _jobs.CheckCriticalAbsences(shutdown.Token);

        _mediatorMock.Verify(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), shutdown.Token), Times.Once);
    }

    public static TheoryData<string, int, AttemptsExceededAction> RetryPolicies => new()
    {
        { nameof(AttendanceJobs.DownloadFromAllDevices), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.PerformAutoBackup), 2, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.SendAutoReport), 2, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.RetryDeviceDownloads), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.CalculatePreviousDay), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.AutoClosePeriods), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.SyncDeviceBiometrics), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.SyncExternalDeviceBiometrics), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.SyncEmployeeBiometrics), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.CheckCriticalAbsences), 0, AttemptsExceededAction.Delete },
        { nameof(AttendanceJobs.MonitorDeviceHealth), 0, AttemptsExceededAction.Delete },
        { nameof(AttendanceJobs.SyncCloudLogs), 0, AttemptsExceededAction.Delete },
        { nameof(AttendanceJobs.CheckScheduleIntegrity), 0, AttemptsExceededAction.Delete },
        { nameof(AttendanceJobs.ProcessAttendance), 10, AttemptsExceededAction.Fail },
    };

    /// <summary>La política efectiva la resuelve Hangfire (global, clase y método): los 10 reintentos por defecto ya no aplican.</summary>
    [Theory]
    [MemberData(nameof(RetryPolicies))]
    public void EachJob_HasAnExplicitRetryPolicy(string method, int attempts, AttemptsExceededAction onExceeded)
    {
        foreach (var info in typeof(AttendanceJobs).GetMethods().Where(m => m.Name == method))
        {
            var job = new Hangfire.Common.Job(typeof(AttendanceJobs), info, info.GetParameters().Select(_ => (object?)null).ToArray());
            var retry = Hangfire.Common.JobFilterProviders.Providers.GetFilters(job).Select(f => f.Instance).OfType<AutomaticRetryAttribute>().Single();

            retry.Attempts.Should().Be(attempts, method);
            retry.OnAttemptsExceeded.Should().Be(onExceeded, method);
        }
    }

    [Fact]
    public void TheDashboard_ShowsTheRightName()
    {
        string Name(string method) => typeof(AttendanceJobs).GetMethods().First(m => m.Name == method)
            .GetCustomAttributes(typeof(JobDisplayNameAttribute), false).Cast<JobDisplayNameAttribute>().Single().DisplayName;

        Name(nameof(AttendanceJobs.CalculatePreviousDay)).Should().Be("Calculate Previous Day Attendance");
        Name(nameof(AttendanceJobs.CheckCriticalAbsences)).Should().Be("Check for Critical Position Absences");
        Name(nameof(AttendanceJobs.AutoClosePeriods)).Should().Be("Auto-close Attendance Periods");
    }

    [Theory]
    [InlineData("America/Mexico_City", -6)]
    [InlineData("America/Tijuana", -8)]
    public void TheScheduleTimeZone_IsTheSystemOne(string timeZoneId, int januaryOffsetHours)
    {
        var zone = HangfireAttendanceJobScheduler.ResolveTimeZone(timeZoneId);

        zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(januaryOffsetHours));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Zona/Inexistente")]
    public void AnUnknownTimeZone_FallsBackToTheServerOne(string? timeZoneId) =>
        HangfireAttendanceJobScheduler.ResolveTimeZone(timeZoneId).Should().Be(TimeZoneInfo.Local);

    // ===== Bloque G: reintentos, alertas y fecha de negocio =====

    private readonly List<AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert> _alerts = new();
    private readonly List<string> _emails = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobs = new();
    private readonly List<(Hangfire.Common.Job Job, Hangfire.States.ScheduledState State)> _scheduled = new();

    private AttendanceJobs JobsWithAlerts()
    {
        var repository = new Mock<AttendanceSystem.Domain.Repositories.ISystemAlertRepository>();
        repository.Setup(r => r.GetActiveAlertsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _alerts.Where(a => !a.IsResolved).ToList());
        repository.Setup(r => r.AddAsync(It.IsAny<AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert, CancellationToken>((a, _) => _alerts.Add(a))
            .Returns(Task.CompletedTask);
        var email = new Mock<global::IEmailService>();
        email.Setup(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<global::AlertLevel>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, global::AlertLevel, CancellationToken>((subject, _, _, _) => _emails.Add(subject))
            .Returns(Task.CompletedTask);
        var alerts = new AttendanceSystem.Application.Features.Automation.OperationalAlerts(repository.Object,
            new Mock<AttendanceSystem.Application.Abstractions.IUnitOfWork>().Object, email.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AttendanceSystem.Application.Features.Automation.OperationalAlerts>.Instance);
        _backgroundJobs.Setup(c => c.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()))
            .Callback<Hangfire.Common.Job, Hangfire.States.IState>((job, state) => _scheduled.Add((job, (Hangfire.States.ScheduledState)state)))
            .Returns("job-id");
        return new AttendanceJobs(_mediatorMock.Object, alerts: alerts, backgroundJobs: _backgroundJobs.Object);
    }

    private void Downloads(params (string Id, bool Success)[] devices) =>
        _mediatorMock.Setup(m => m.Send(It.IsAny<DownloadFromAllDevicesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DownloadResultDto>>.Success(devices
                .Select(d => new DownloadResultDto(d.Id, 0, DateTime.UtcNow, Success: d.Success, ErrorMessage: d.Success ? null : "sin respuesta"))
                .ToList()));

    // 15/10/2026 05:30 UTC = 14/10/2026 23:30 en America/Mexico_City.
    private static readonly AttendanceJobs.JobRun LateEvening = new(new DateTime(2026, 10, 15, 5, 30, 0, DateTimeKind.Utc), 0);

    [Fact]
    public async Task ADailyDownloadWithFailedClocks_Succeeds_AndRetriesOnlyThoseIn10Minutes()
    {
        var jobs = JobsWithAlerts();
        Downloads(("R1", true), ("R2", false), ("R3", false));

        await jobs.DownloadFromAllDevicesAsync(LateEvening, CancellationToken.None);

        var (job, state) = _scheduled.Single();
        job.Method.Name.Should().Be(nameof(AttendanceJobs.RetryDeviceDownloads));
        ((string[])job.Args[0]!).Should().Equal("R2", "R3");
        job.Args[1].Should().Be(1);
        job.Args[2].Should().Be(new DateTime(2026, 10, 14));
        state.EnqueueAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));
        _alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheFirstRetry_DownloadsOnlyTheFailedClocks_AndSchedulesTheSecondIn30Minutes()
    {
        var jobs = JobsWithAlerts();
        Downloads(("R2", false));

        await jobs.RetryDeviceDownloadsAsync(new[] { "R2", "R3" }, 1, new DateTime(2026, 10, 14), CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(It.Is<DownloadFromAllDevicesCommand>(c => c.DeviceIds!.SequenceEqual(new[] { "R2", "R3" })), It.IsAny<CancellationToken>()), Times.Once);
        var (job, state) = _scheduled.Single();
        ((string[])job.Args[0]!).Should().Equal("R2");
        job.Args[1].Should().Be(2);
        state.EnqueueAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AfterTheSecondRetry_ClocksStillDown_LeaveOneAlert()
    {
        var jobs = JobsWithAlerts();
        Downloads(("R2", false));

        await jobs.RetryDeviceDownloadsAsync(new[] { "R2" }, 2, new DateTime(2026, 10, 14), CancellationToken.None);

        _scheduled.Should().BeEmpty();
        _alerts.Should().ContainSingle(a => a.ReferenceId == "relojes:20261014").Which.Message.Should().Contain("R2").And.Contain("sin respuesta");
        _emails.Should().ContainSingle();
    }

    [Fact]
    public async Task ARetryThatRecovers_LeavesNoAlert()
    {
        var jobs = JobsWithAlerts();
        Downloads(("R2", true));

        await jobs.RetryDeviceDownloadsAsync(new[] { "R2" }, 2, new DateTime(2026, 10, 14), CancellationToken.None);

        _scheduled.Should().BeEmpty();
        _alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task AFullDownload_ResolvesThePreviousFailedClocksAlert()
    {
        var jobs = JobsWithAlerts();
        _alerts.Add(AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert.Create(AttendanceSystem.Domain.Enumerations.AlertType.SystemError, "relojes:20261013", "R2 caído"));
        Downloads(("R1", true), ("R2", true));

        await jobs.DownloadFromAllDevicesAsync(LateEvening, CancellationToken.None);

        _alerts.Should().OnlyContain(a => a.IsResolved);
    }

    [Fact]
    public async Task ABackupThatFails_IsRetried_AndOnlyTheLastFailureLeavesAnAlert()
    {
        var jobs = JobsWithAlerts();
        _mediatorMock.Setup(m => m.Send(It.IsAny<AttendanceSystem.Application.Features.Backup.Commands.CreateBackupCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = false, Message = "disco lleno" });

        await jobs.Invoking(j => j.PerformAutoBackupAsync(LateEvening, CancellationToken.None)).Should().ThrowAsync<JobExecutionFailedException>();
        await jobs.Invoking(j => j.PerformAutoBackupAsync(LateEvening with { RetryCount = 1 }, CancellationToken.None)).Should().ThrowAsync<JobExecutionFailedException>();
        _alerts.Should().BeEmpty("aún le quedan reintentos");

        await jobs.Invoking(j => j.PerformAutoBackupAsync(LateEvening with { RetryCount = 2 }, CancellationToken.None)).Should().ThrowAsync<JobExecutionFailedException>();

        _alerts.Should().ContainSingle(a => a.ReferenceId == "tarea:automated-database-backup:20261014")
            .Which.Message.Should().Contain("después de 2 reintentos").And.Contain("disco lleno");
        _emails.Should().ContainSingle();
    }

    [Fact]
    public async Task ABackupThatSucceeds_ResolvesItsPreviousFailureAlerts()
    {
        var jobs = JobsWithAlerts();
        _alerts.Add(AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert.Create(AttendanceSystem.Domain.Enumerations.AlertType.SystemError, "tarea:automated-database-backup:20261013", "falló"));
        _alerts.Add(AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert.Create(AttendanceSystem.Domain.Enumerations.AlertType.SystemError, "tarea:automated-daily-report:20261013", "falló"));
        _mediatorMock.Setup(m => m.Send(It.IsAny<AttendanceSystem.Application.Features.Backup.Commands.CreateBackupCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupResultDto { Success = true });

        await jobs.PerformAutoBackupAsync(LateEvening, CancellationToken.None);

        _alerts.Single(a => a.ReferenceId!.Contains("backup")).IsResolved.Should().BeTrue();
        _alerts.Single(a => a.ReferenceId!.Contains("report")).IsResolved.Should().BeFalse("sólo resuelve las suyas");
    }

    [Fact]
    public async Task TheReport_UsesTheDateItWasTriggered_InTheSystemTimeZone_EvenOnARetryAfterMidnight()
    {
        var jobs = JobsWithAlerts();
        _mediatorMock.Setup(m => m.Send(It.IsAny<AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport.SendDailyReportCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        // El reintento corre pasada la medianoche, pero el trabajo se disparó el 14 a las 23:30.
        await jobs.SendAutoReportAsync(LateEvening with { RetryCount = 1 }, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(It.Is<AttendanceSystem.Application.Features.Automation.Commands.SendDailyReport.SendDailyReportCommand>(c => c.Today == new DateTime(2026, 10, 14)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
