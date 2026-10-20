using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Type)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Content)
            .IsRequired();

        builder.Property(x => x.OccurredOnUtc)
            .IsRequired();

        builder.Property(x => x.ProcessedOnUtc)
            .IsRequired(false);

        builder.Property(x => x.Error)
            .IsRequired(false);

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.LockedBy).HasMaxLength(200);
        builder.Property(x => x.LockedUntilUtc);
        builder.Property(x => x.NextAttemptOnUtc);
        builder.Property(x => x.DeadLetteredOnUtc);

        builder.HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc });
        builder.HasIndex(x => new { x.ProcessedOnUtc, x.DeadLetteredOnUtc, x.NextAttemptOnUtc });
    }
}
