using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class SystemConfigurationConfiguration : IEntityTypeConfiguration<SystemConfiguration>
{
    public void Configure(EntityTypeBuilder<SystemConfiguration> builder)
    {
        builder.ToTable("SystemConfiguration"); // Singular table name for singleton

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CompanyName)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("Mi Empresa");

        builder.Property(c => c.CompanyLogo)
            .IsRequired(false);

        builder.Property(c => c.LateTolerance)
            .IsRequired();

        builder.Property(c => c.StandardWorkHours)
            .IsRequired();

        builder.Property(c => c.AutoClearDevicesAfterDownload)
            .IsRequired();

        builder.Property(c => c.IsAutoDownloadEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.AutoDownloadTime);

        // Work Period Configuration
        builder.Property(c => c.WorkPeriodMode)
            .IsRequired();

        builder.Property(c => c.WeeklyStartDay)
            .IsRequired();

        builder.Property(c => c.FortnightFirstDay)
            .IsRequired();

        builder.Property(c => c.FortnightSecondDay)
            .IsRequired();

        builder.Property(c => c.MonthlyStartDay)
            .IsRequired();

        // F06-05: regla de comida global.
        builder.Property(c => c.LunchWindowStart).IsRequired().HasDefaultValue(new TimeSpan(12, 30, 0));
        builder.Property(c => c.LunchWindowEnd).IsRequired().HasDefaultValue(new TimeSpan(15, 0, 0));
        builder.Property(c => c.PaidLunchMinutes).IsRequired().HasDefaultValue(30);
        builder.Property(c => c.LunchWindowEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(c => c.DoubleTapMinutes).IsRequired().HasDefaultValue(15);
        builder.Property(c => c.QuarantineMinDateDaysBeforeDevice).IsRequired().HasDefaultValue(30);
        builder.Property(c => c.QuarantineFutureToleranceMinutes).IsRequired().HasDefaultValue(5);
        builder.Property(c => c.QuarantineBackwardJumpMinutes).IsRequired().HasDefaultValue(60);
        builder.Property(c => c.QuarantineRealtimeLagMinutes).IsRequired().HasDefaultValue(30);
        builder.Property(c => c.QuarantineClockDriftAlertMinutes).IsRequired().HasDefaultValue(10);
        builder.Property(c => c.QuarantineSdkReadMarginDays).IsRequired().HasDefaultValue(1);
        builder.Property(c => c.QuarantineClockSyncToleranceMinutes).IsRequired().HasDefaultValue(2);
        builder.Ignore(c => c.QuarantinePolicy);
        builder.Property(c => c.NightLunchWindowStart).IsRequired().HasDefaultValue(new TimeSpan(23, 0, 0));
        builder.Property(c => c.NightLunchWindowEnd).IsRequired().HasDefaultValue(new TimeSpan(4, 0, 0));
        builder.Ignore(c => c.LunchPolicy);
        builder.Ignore(c => c.NightLunchPolicy);

        builder.Property(c => c.AreAlertsEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.AbsenceAlertEmails);
        builder.Property(c => c.LateAlertEmails);
        builder.Property(c => c.SystemFailureAlertEmails);

        // Backup Automation
        builder.Property(c => c.IsAutoBackupEnabled)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(c => c.AutoBackupTime);

        // Report Automation
        builder.Property(c => c.IsAutoReportEnabled)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(c => c.AutoReportTime);
        builder.Property(c => c.AutoReportEmails);
        builder.Property(c => c.AutoReportForToday)
            .IsRequired()
            .HasDefaultValue(false);

        // Cloud Sync Settings
        builder.Property(c => c.IsCloudSyncEnabled)
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(c => c.CloudDbConnectionString)
            .IsRequired(false);

        // Timezone Configuration
        builder.Property(c => c.DefaultTimeZoneId)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("America/Mexico_City");
    }
}
