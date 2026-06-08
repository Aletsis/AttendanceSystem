using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class SystemAlertConfiguration : IEntityTypeConfiguration<SystemAlert>
{
    public void Configure(EntityTypeBuilder<SystemAlert> builder)
    {
        builder.ToTable("SystemAlerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(
                id => id.Value,
                value => SystemAlertId.From(value));

        builder.Property(a => a.Message)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.ReferenceId)
            .HasMaxLength(100);
            
        builder.Property(a => a.Type)
            .HasConversion<int>();
    }
}
