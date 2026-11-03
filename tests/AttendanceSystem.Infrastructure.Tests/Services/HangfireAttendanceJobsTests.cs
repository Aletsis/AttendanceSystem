using AttendanceSystem.Application.Common;
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
        { nameof(AttendanceJobs.PerformAutoBackup), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.SendAutoReport), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.CalculatePreviousDay), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.AutoClosePeriods), 0, AttemptsExceededAction.Fail },
        { nameof(AttendanceJobs.SyncDeviceBiometrics), 0, AttemptsExceededAction.Fail },
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
}
