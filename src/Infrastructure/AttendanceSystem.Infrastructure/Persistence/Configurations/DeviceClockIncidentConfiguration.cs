using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

/// <summary>Caso 18: periodos en que un reloj estuvo atrasado (en horas reportadas por el reloj).</summary>
public class DeviceClockIncidentConfiguration : IEntityTypeConfiguration<DeviceClockIncident>
{
    public void Configure(EntityTypeBuilder<DeviceClockIncident> builder)
    {
        builder.ToTable("DeviceClockIncidents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DeviceId).HasConversion(id => id.Value, value => DeviceId.From(value)).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ReportedFrom).IsRequired();
        builder.Property(x => x.ReportedTo).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => x.DeviceId);
    }
}
