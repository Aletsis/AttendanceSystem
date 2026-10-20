using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AttendanceAdjustmentHistoryConfiguration : IEntityTypeConfiguration<AttendanceAdjustmentHistory>
{
    public void Configure(EntityTypeBuilder<AttendanceAdjustmentHistory> builder)
    {
        builder.ToTable("AttendanceAdjustmentHistory");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AdjustmentId).IsRequired();
        builder.Property(x => x.EmployeeId)
            .HasConversion(id => id.Value, value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.Date).HasColumnType("date").IsRequired();
        builder.Property(x => x.Revision).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Before).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.After).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.ChangedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ChangedAt).IsRequired();

        builder.HasIndex(x => new { x.EmployeeId, x.Date, x.Revision });
        builder.HasIndex(x => x.AdjustmentId);
    }
}
