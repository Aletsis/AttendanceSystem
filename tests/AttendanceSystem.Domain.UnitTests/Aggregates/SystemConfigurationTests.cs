using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class SystemConfigurationTests
{
    [Fact]
    public void CreateDefault_ShouldInitializeWithSensibleDefaults()
    {
        // Act
        var config = SystemConfiguration.CreateDefault();

        // Assert
        config.Should().NotBeNull();
        config.Id.Should().Be(SystemConfiguration.ConfigurationId);
        config.CompanyName.Should().Be("Mi Empresa");
        config.LateTolerance.Should().Be(TimeSpan.FromMinutes(15));
        config.StandardWorkHours.Should().Be(TimeSpan.FromHours(8));
        config.WorkPeriodMode.Should().Be(WorkPeriodMode.Weekly);
        config.AdmsPort.Should().Be(18373);
        config.SmtpPort.Should().Be(587);
        config.SmtpEnableSsl.Should().BeTrue();
    }

    [Fact]
    public void UpdateSettings_ShouldModifyValues()
    {
        // Arrange
        var config = SystemConfiguration.CreateDefault();

        // Act
        config.UpdateSettings(
            companyName: "Acme Corp",
            companyLogo: new byte[] { 1, 2, 3 },
            lateTolerance: TimeSpan.FromMinutes(10),
            standardWorkHours: TimeSpan.FromHours(8.5),
            autoClearDevicesAfterDownload: true,
            isAutoDownloadEnabled: true,
            autoDownloadTime: TimeSpan.FromHours(23),
            autoDownloadOnlyToday: true,
            admsPort: 19000,
            backupDirectory: "/custom/backups",
            backupTimeoutMinutes: 15,
            areAlertsEnabled: true,
            absenceAlertEmails: "alert@acme.com",
            lateAlertEmails: "late@acme.com",
            systemFailureAlertEmails: "sys@acme.com",
            smtpHost: "smtp.acme.com",
            smtpPort: 465,
            smtpUser: "user",
            smtpPassword: "pass",
            smtpEnableSsl: true,
            isAutoBackupEnabled: true,
            autoBackupTime: TimeSpan.FromHours(2),
            isAutoReportEnabled: true,
            autoReportTime: TimeSpan.FromHours(6),
            autoReportEmails: "reports@acme.com",
            autoReportForToday: false);

        // Assert
        config.CompanyName.Should().Be("Acme Corp");
        config.LateTolerance.Should().Be(TimeSpan.FromMinutes(10));
        config.AutoClearDevicesAfterDownload.Should().BeTrue();
        config.IsAutoDownloadEnabled.Should().BeTrue();
        config.BackupDirectory.Should().Be("/custom/backups");
        config.AreAlertsEnabled.Should().BeTrue();
        config.AbsenceAlertEmails.Should().Be("alert@acme.com");
    }

    [Fact]
    public void UpdateWorkPeriodSettings_ShouldModifyPeriodSettings()
    {
        // Arrange
        var config = SystemConfiguration.CreateDefault();

        // Act
        config.UpdateWorkPeriodSettings(
            mode: WorkPeriodMode.Fortnightly,
            weeklyStartDay: DayOfWeek.Monday,
            fortnightFirstDay: 1,
            fortnightSecondDay: 16,
            monthlyStartDay: 1);

        // Assert
        config.WorkPeriodMode.Should().Be(WorkPeriodMode.Fortnightly);
        config.FortnightFirstDay.Should().Be(1);
        config.FortnightSecondDay.Should().Be(16);
    }
}
