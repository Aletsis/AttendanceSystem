using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Configuration.Commands.UpdateSystemConfiguration;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Configuration;

public class UpdateSystemConfigurationCommandHandlerTests
{
    private readonly Mock<ISystemConfigurationRepository> _repositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAttendanceJobScheduler> _jobSchedulerMock;
    private readonly UpdateSystemConfigurationCommandHandler _handler;

    public UpdateSystemConfigurationCommandHandlerTests()
    {
        _repositoryMock = new Mock<ISystemConfigurationRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _jobSchedulerMock = new Mock<IAttendanceJobScheduler>();

        _handler = new UpdateSystemConfigurationCommandHandler(
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            _jobSchedulerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValidParameters_ShouldUpdateConfigAndScheduleJobs()
    {
        // Arrange
        var existingConfig = SystemConfiguration.CreateDefault();

        _repositoryMock
            .Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingConfig);

        var autoDownloadTime = TimeSpan.FromHours(23);
        var autoBackupTime = TimeSpan.FromHours(2);
        var autoReportTime = TimeSpan.FromHours(6);

        var command = new UpdateSystemConfigurationCommand(
            CompanyName: "Empresa Actualizada",
            CompanyLogo: null,
            LateTolerance: TimeSpan.FromMinutes(10),
            StandardWorkHours: TimeSpan.FromHours(8),
            AutoClearDevicesAfterDownload: true,
            IsAutoDownloadEnabled: true,
            AutoDownloadTime: autoDownloadTime,
            AutoDownloadOnlyToday: false,
            AdmsPort: 18373,
            BackupDirectory: "/data/backups",
            BackupTimeoutMinutes: 10,
            WorkPeriodMode: WorkPeriodMode.Weekly,
            WeeklyStartDay: DayOfWeek.Monday,
            FortnightFirstDay: 1,
            FortnightSecondDay: 16,
            MonthlyStartDay: 1,
            AreAlertsEnabled: true,
            AbsenceAlertEmails: "faltas@empresa.com",
            LateAlertEmails: null,
            SystemFailureAlertEmails: null,
            SmtpHost: "smtp.mail.com",
            SmtpPort: 587,
            SmtpUser: "user",
            SmtpPassword: "password",
            SmtpEnableSsl: true,
            IsAutoBackupEnabled: true,
            AutoBackupTime: autoBackupTime,
            IsAutoReportEnabled: true,
            AutoReportTime: autoReportTime,
            AutoReportEmails: "reportes@empresa.com",
            AutoReportForToday: false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existingConfig.Id);
        existingConfig.CompanyName.Should().Be("Empresa Actualizada");
        existingConfig.LateTolerance.Should().Be(TimeSpan.FromMinutes(10));

        _jobSchedulerMock.Verify(s => s.ScheduleAutoDownload(autoDownloadTime), Times.Once);
        _jobSchedulerMock.Verify(s => s.ScheduleAutoBackup(autoBackupTime), Times.Once);
        _jobSchedulerMock.Verify(s => s.ScheduleAutoReport(autoReportTime), Times.Once);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
