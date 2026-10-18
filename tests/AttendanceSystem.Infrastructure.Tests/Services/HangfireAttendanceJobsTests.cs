using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;
using AttendanceSystem.Application.Features.CloudSync.Commands.SyncCloudLogs;
using AttendanceSystem.Infrastructure.Exceptions;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
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
        _mediatorMock.Setup(m => m.Send(It.IsAny<CheckCriticalAbsencesCommand>(), default))
            .ReturnsAsync(Result<int>.Failure("Error simulado de ausencias críticas"));

        // Act
        var act = () => _jobs.CheckCriticalAbsences();

        // Assert
        var ex = await act.Should().ThrowAsync<JobExecutionFailedException>();
        ex.Which.JobName.Should().Be("Check for Critical Position Absences");
        ex.Which.Message.Should().Contain("Error simulado de ausencias críticas");
    }

    [Fact]
    public async Task SyncCloudLogs_WhenFails_ShouldThrowJobExecutionFailedException()
    {
        // Arrange
        _mediatorMock.Setup(m => m.Send(It.IsAny<SyncCloudLogsCommand>(), default))
            .ReturnsAsync(Result<SyncCloudLogsResultDto>.Failure("Error en sincronización en la nube"));

        // Act
        var act = () => _jobs.SyncCloudLogs();

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
}
