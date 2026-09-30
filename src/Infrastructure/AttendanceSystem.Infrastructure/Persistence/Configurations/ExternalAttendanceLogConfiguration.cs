using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class ExternalAttendanceLogConfiguration : IEntityTypeConfiguration<ExternalAttendanceLog>
{
    public void Configure(EntityTypeBuilder<ExternalAttendanceLog> builder)
    {
        builder.ToTable("ExternalAttendanceLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.BranchCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.EmployeeId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.CheckTime)
            .IsRequired();

        builder.Property(x => x.VerifyMethod)
            .IsRequired();

        builder.Property(x => x.CheckType)
            .IsRequired();

        builder.Property(x => x.SourceDevice)
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasConversion(
                s => s.Id,
                value => ExternalLogStatus.FromValue(value))
            .HasColumnName("StatusId")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.TransferredAt);

        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => new { x.BranchCode, x.Status });
        builder.HasIndex(x => x.CreatedAt);
    }
}
