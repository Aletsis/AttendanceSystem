using AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

/// <summary>Caso 17: cierres de periodo de asistencia.</summary>
public class AttendancePeriodClosureConfiguration : IEntityTypeConfiguration<AttendancePeriodClosure>
{
    public void Configure(EntityTypeBuilder<AttendancePeriodClosure> builder)
    {
        builder.ToTable("AttendancePeriodClosures");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StartDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.EndDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.ClosedBy).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ReopenedBy).HasMaxLength(256);
        builder.Property(x => x.ReopenReason).HasMaxLength(500);
        builder.Ignore(x => x.IsActive);
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.StartDate, x.EndDate });
    }
}
