using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class ExternalEmployeeConfiguration : IEntityTypeConfiguration<ExternalEmployee>
{
    public void Configure(EntityTypeBuilder<ExternalEmployee> builder)
    {
        builder.ToTable("ExternalEmployees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.BranchId)
            .HasConversion(
                id => id.Value,
                value => BranchId.From(value))
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EmployeeNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Email)
            .IsRequired(false)
            .HasMaxLength(255);

        builder.Property(e => e.PhoneNumber)
            .IsRequired(false)
            .HasMaxLength(20);

        builder.Property(e => e.Position)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(e => e.Department)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.CardNumber)
            .IsRequired(false)
            .HasMaxLength(50);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false);

        // Índice único compuesto: En una misma sucursal externa no se puede repetir el número de empleado
        builder.HasIndex(e => new { e.BranchId, e.EmployeeNumber })
            .IsUnique();

        builder.HasIndex(e => e.BranchId);
    }
}
