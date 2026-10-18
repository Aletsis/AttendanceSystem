using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AdmsIngestionIncidentConfiguration : IEntityTypeConfiguration<AdmsIngestionIncident>
{
    public void Configure(EntityTypeBuilder<AdmsIngestionIncident> builder)
    {
        builder.ToTable("AdmsIngestionIncidents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.SerialNumber)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.RawData)
            .IsRequired();

        builder.Property(x => x.Error)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.Resolved)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(x => new { x.SerialNumber, x.Resolved });
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
