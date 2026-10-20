using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class PunchAssignmentConfiguration : IEntityTypeConfiguration<PunchAssignment>
{
    public void Configure(EntityTypeBuilder<PunchAssignment> builder)
    {
        builder.ToTable("PunchAssignments", t =>
        {
            t.HasCheckConstraint("CK_PunchAssignments_Role", "\"Role\" BETWEEN 1 AND 8");
            t.HasCheckConstraint("CK_PunchAssignments_Origin", "\"Origin\" IN (1, 2)");
        });

        // F02-02: la clave es el marcaje; un marcaje no puede ocupar dos posiciones ni dos jornadas.
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("AttendanceRecordId")
            .HasConversion(
                id => id.Value,
                value => AttendanceRecordId.From(value))
            .ValueGeneratedNever();

        builder.Ignore(x => x.AttendanceRecordId);
        builder.Ignore(x => x.IsPosition);

        builder.Property(x => x.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.WorkDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.Role).HasConversion<int>().IsRequired();
        builder.Property(x => x.Origin).HasConversion<int>().IsRequired();
        builder.Property(x => x.AssignedAt).IsRequired();

        // F02-02: pertenencia. La clave foránea compuesta (marcaje, empleado) impide relacionar
        // un marcaje con una jornada de otro empleado.
        builder.HasOne<AttendanceRecord>()
            .WithMany()
            .HasForeignKey(x => new { x.Id, x.EmployeeId })
            .HasPrincipalKey(r => new { r.Id, r.EmployeeId })
            .HasConstraintName("FK_PunchAssignments_AttendanceRecords_Owner")
            .OnDelete(DeleteBehavior.Cascade);

        // F03-03: concurrencia optimista con la columna de sistema xmin (no requiere migración). Detecta
        // escrituras sobre datos obsoletos de cualquier escritor que no haya tomado el bloqueo del empleado.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate });
    }
}
