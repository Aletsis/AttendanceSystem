using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("Shifts");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(
                id => id.Value,
                value => ShiftId.From(value));

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.StartTime)
            .IsRequired();

        builder.Property(s => s.ToleranceMinutes)
            .IsRequired();

        builder.Property(s => s.WorkHours)
            .IsRequired();
            
        builder.Property(s => s.ShiftType)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(s => s.EndTime)
            .IsRequired();
    }
}
