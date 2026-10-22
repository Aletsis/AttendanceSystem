using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class QuarantinedPunchConfiguration : IEntityTypeConfiguration<QuarantinedPunch>
{
    public void Configure(EntityTypeBuilder<QuarantinedPunch> builder)
    {
        builder.ToTable("QuarantinedPunches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmployeeId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DeviceId).HasConversion(id => id.Value, value => DeviceId.From(value)).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ReportedTime).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.ResolvedBy).HasMaxLength(100);
        builder.Property(x => x.ResolutionNote).HasMaxLength(500);
        builder.Property(x => x.ReleasedRecordId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value, value => value == null ? null : AttendanceRecordId.From(value.Value));

        builder.HasIndex(x => new { x.Status, x.ReceivedAtUtc });
        builder.HasIndex(x => new { x.DeviceId, x.EmployeeId, x.ReportedTime }).IsUnique();
    }
}
