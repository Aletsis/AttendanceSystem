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
    }
}
