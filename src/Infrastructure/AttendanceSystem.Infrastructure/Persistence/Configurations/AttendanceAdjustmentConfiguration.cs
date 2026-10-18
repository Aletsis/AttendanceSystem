using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AttendanceAdjustmentConfiguration : IEntityTypeConfiguration<AttendanceAdjustment>
{
    public void Configure(EntityTypeBuilder<AttendanceAdjustment> builder)
    {
        builder.ToTable("AttendanceAdjustments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.IsRestDayOverride);

        builder.Property(x => x.ShiftIdOverride)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? ShiftId.From(value.Value) : null);

        builder.Property(x => x.CheckInRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.CheckInTimeOverride);

        builder.Property(x => x.CheckOutRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.CheckOutTimeOverride);

        builder.Property(x => x.Block1CheckOutRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.Block1CheckOutTimeOverride);

        builder.Property(x => x.Block2CheckInRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.Block2CheckInTimeOverride);

        builder.Property(x => x.TemporaryExitStatus)
            .HasConversion<int?>();

        builder.Property(x => x.TemporaryExitNote)
            .HasMaxLength(500);

        builder.Property(x => x.OvertimeAuthorizedOverride);
        builder.Property(x => x.CalculateOvertimeBeforeEntryOverride);

        builder.Property(x => x.Reason)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(100);

        builder.Property(x => x.UpdatedAt);

        // Índice único: solo un ajuste por empleado y fecha
        builder.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        builder.HasIndex(x => x.Date);
    }
}
