using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class PunchTimeCorrectionConfiguration : IEntityTypeConfiguration<PunchTimeCorrection>
{
    public void Configure(EntityTypeBuilder<PunchTimeCorrection> builder)
    {
        builder.ToTable("PunchTimeCorrections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DeviceId).HasConversion(id => id.Value, value => DeviceId.From(value)).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.AppliedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.AppliedAtUtc);

        builder.HasMany(x => x.Entries).WithOne().HasForeignKey(e => e.CorrectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PunchTimeCorrectionEntryConfiguration : IEntityTypeConfiguration<PunchTimeCorrectionEntry>
{
    public void Configure(EntityTypeBuilder<PunchTimeCorrectionEntry> builder)
    {
        builder.ToTable("PunchTimeCorrectionEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AttendanceRecordId).HasConversion(id => id.Value, value => AttendanceRecordId.From(value)).IsRequired();
        builder.Property(x => x.EmployeeId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OldTimeZoneId).HasMaxLength(100);
        builder.Property(x => x.NewTimeZoneId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.AttendanceRecordId);
    }
}
