using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AdmsCommandConfiguration : IEntityTypeConfiguration<AdmsCommand>
{
    public void Configure(EntityTypeBuilder<AdmsCommand> builder)
    {
        builder.ToTable("AdmsCommands");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.SerialNumber)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CommandId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CommandText)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IsDestructive)
            .IsRequired();

        builder.Property(x => x.DownloadLogId)
            .IsRequired(false);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.SentAtUtc)
            .IsRequired(false);

        builder.Property(x => x.ConfirmedAtUtc)
            .IsRequired(false);

        builder.Property(x => x.ReturnCode)
            .IsRequired(false);

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .IsRequired(false);

        builder.HasIndex(x => new { x.SerialNumber, x.Status });
        builder.HasIndex(x => new { x.SerialNumber, x.CommandId });
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
