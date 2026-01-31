using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class DailyAttendanceConfiguration : IEntityTypeConfiguration<DailyAttendance>
{
    public void Configure(EntityTypeBuilder<DailyAttendance> builder)
    {
        builder.ToTable("DailyAttendances");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => DailyAttendanceId.From(value))
            .ValueGeneratedNever();

        builder.Property(x => x.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Date)
            .HasColumnType("date")
            .IsRequired();

        // Shift Snapshot
        builder.Property(x => x.ShiftId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? ShiftId.From(value.Value) : null);

        builder.Property(x => x.ShiftName)
            .HasMaxLength(100);

        builder.Property(x => x.ScheduledCheckIn);
        builder.Property(x => x.ScheduledCheckOut);
        builder.Property(x => x.ToleranceMinutes);

        // Actuals
        builder.Property(x => x.ActualCheckIn);
        builder.Property(x => x.CheckInRecordId)
             .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.ActualCheckOut);
        builder.Property(x => x.CheckOutRecordId)
             .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        // Calculated
        builder.Property(x => x.IsAbsent);
        builder.Property(x => x.LateMinutes);
        builder.Property(x => x.EarlyDepartureMinutes);
        builder.Property(x => x.OvertimeMinutes);
        builder.Property(x => x.MissingCheckIn);
        builder.Property(x => x.MissingCheckOut);
        builder.Property(x => x.IsRestDay);
        builder.Property(x => x.WorkedOnRestDay);

        builder.Ignore(x => x.DomainEvents);

        // Indexes for performance (Reporting by Date/Employee)
        builder.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        builder.HasIndex(x => x.Date);
    }
}
