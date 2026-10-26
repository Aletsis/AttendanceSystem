using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class RotationPatternConfiguration : IEntityTypeConfiguration<RotationPattern>
{
    public void Configure(EntityTypeBuilder<RotationPattern> builder)
    {
        builder.ToTable("RotationPatterns");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => RotationPatternId.From(value))
            .ValueGeneratedNever();

        builder.Property(x => x.EmployeeId)
            .HasConversion(id => id.Value, value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.StartDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.EndDate).HasColumnType("date");
        builder.Property(x => x.GeneratedThrough).HasColumnType("date").IsRequired();
        builder.Property(x => x.Definition).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.Ignore(x => x.IsIndefinite);
        builder.Ignore(x => x.DomainEvents);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Un patrón vigente por empleado: un esquema nuevo reemplaza al anterior.
        builder.HasIndex(x => x.EmployeeId).IsUnique();
    }
}
