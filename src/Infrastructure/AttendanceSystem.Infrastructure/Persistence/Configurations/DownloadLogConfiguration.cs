using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class DownloadLogConfiguration : IEntityTypeConfiguration<DownloadLog>
{
    public void Configure(EntityTypeBuilder<DownloadLog> builder)
    {
        builder.ToTable("DownloadLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => DownloadLogId.From(value))
            .ValueGeneratedNever();

        builder.Property(x => x.DeviceId)
            .HasConversion(
                id => id.Value,
                value => DeviceId.From(value))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.IsSuccessful)
            .IsRequired();

        builder.Property(x => x.TotalRecordsDownloaded)
            .IsRequired();

        builder.Property(x => x.NewRecordsAdded)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.DownloadType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.InitiatedByUserId)
            .HasMaxLength(450); // ASP.NET Identity default user ID length

        builder.Property(x => x.InitiatedByUserName)
            .HasMaxLength(256); // ASP.NET Identity default username length

        builder.Property(x => x.FromDate);

        builder.Property(x => x.ToDate);

        builder.Property(x => x.DurationMs)
            .IsRequired();

        // Ignorar eventos de dominio (no se persisten)
        builder.Ignore(x => x.DomainEvents);

        // Índices para mejorar consultas
        builder.HasIndex(x => x.DeviceId);
        builder.HasIndex(x => x.StartedAt);
        builder.HasIndex(x => x.DownloadType);
        builder.HasIndex(x => x.IsSuccessful);
    }
}
