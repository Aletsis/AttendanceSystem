using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AttendanceConsolidationRequestConfiguration : IEntityTypeConfiguration<AttendanceConsolidationRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceConsolidationRequest> builder)
    {
        builder.ToTable("AttendanceConsolidationRequests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.StartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.EndDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.RequestedAt)
            .IsRequired();

        builder.Property(x => x.ProcessedAt);
        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.LastRequestedAt).IsRequired();
        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1);
        builder.Property(x => x.ClaimedBy).HasMaxLength(200);
        builder.Property(x => x.LeaseExpiresAt);
        builder.Property(x => x.ClaimedVersion);
        builder.Property(x => x.NextAttemptAt);
        builder.Ignore(x => x.IsActive);

        builder.HasIndex(x => new { x.Status, x.RequestedAt });
        builder.HasIndex(x => new { x.EmployeeId, x.Status });

        // F04-04: como máximo una solicitud activa (Pending o Processing) por empleado.
        builder.HasIndex(x => x.EmployeeId)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)")
            .HasDatabaseName("IX_AttendanceConsolidationRequests_EmployeeId_Active");
    }
}
