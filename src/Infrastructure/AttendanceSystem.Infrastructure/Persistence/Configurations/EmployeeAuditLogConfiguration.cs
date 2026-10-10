namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class EmployeeAuditLogConfiguration : IEntityTypeConfiguration<EmployeeAuditLog>
{
    public void Configure(EntityTypeBuilder<EmployeeAuditLog> builder)
    {
        builder.ToTable("EmployeeAuditLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.Timestamp)
            .IsRequired();

        builder.Property(l => l.UserId)
            .IsRequired(false)
            .HasMaxLength(450);

        builder.Property(l => l.UserName)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.Property(l => l.Details)
            .IsRequired(false)
            .HasMaxLength(1000);

        builder.Property(l => l.ChangesJson)
            .IsRequired(false)
            .HasColumnType("text");

        // Índices para consultas eficientes
        builder.HasIndex(l => l.EmployeeId);
        builder.HasIndex(l => new { l.EmployeeId, l.Timestamp });
    }
}
