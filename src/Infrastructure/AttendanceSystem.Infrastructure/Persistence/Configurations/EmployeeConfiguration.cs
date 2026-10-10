using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20);

        builder.Property(e => e.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Email)
            .IsRequired(false)
            .HasMaxLength(255);

        builder.HasIndex(e => e.Email)
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        builder.Property(e => e.PhoneNumber)
            .IsRequired(false)
            .HasMaxLength(20);

        builder.Property(e => e.HireDate)
            .IsRequired();

        builder.Property(e => e.Gender)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        // Configuración de relaciones con value objects
        builder.Property(e => e.BranchId)
            .HasConversion(
                id => id.Value,
                value => BranchId.From(value))
            .IsRequired();

        builder.Property(e => e.DepartmentId)
            .HasConversion(
                id => id.Value,
                value => DepartmentId.From(value))
            .IsRequired();

        builder.Property(e => e.PositionId)
            .HasConversion(
                id => id.Value,
                value => PositionId.From(value))
            .IsRequired();

        builder.Property(e => e.ShiftType)
            .HasConversion<int?>()
            .IsRequired(false);

        builder.Property(e => e.ScheduleId)
            .HasConversion(
                id => id!.Value,
                value => ShiftId.From(value))
            .IsRequired(false);

        builder.Property(e => e.RestDay)
            .HasConversion<int?>()
            .IsRequired(false);

        var weekDayListComparer = new ValueComparer<List<WeekDay>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        builder.Property(e => e.RestDays)
            .HasConversion(
                days => days != null && days.Count > 0 ? string.Join(",", days.Select(d => (int)d)) : null,
                str => string.IsNullOrWhiteSpace(str)
                    ? new List<WeekDay>()
                    : str.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => (WeekDay)int.Parse(s))
                         .ToList(),
                weekDayListComparer)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(e => e.OvertimeAuthorized)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.OvertimeCalculationMethod)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(AttendanceSystem.Domain.Enumerations.OvertimeCalculationMethod.NoRounding);

        builder.Property(e => e.OvertimeCapType)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(AttendanceSystem.Domain.Enumerations.OvertimeCapType.None);

        builder.Property(e => e.OvertimeCapMinutes)
            .IsRequired(false);

        builder.Property(e => e.DevicePrivilege)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(AttendanceSystem.Domain.Enumerations.DevicePrivilege.User);

        // Configuración de auditoría
        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.CreatedBy)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false);

        builder.Property(e => e.UpdatedBy)
            .IsRequired(false)
            .HasMaxLength(256);

        // Índices para mejorar el rendimiento de las consultas
        builder.HasIndex(e => e.BranchId);
        builder.HasIndex(e => e.DepartmentId);
        builder.HasIndex(e => e.PositionId);
        builder.Property(e => e.CardNumber)
            .IsRequired(false)
            .HasMaxLength(50); // Tarjetas RFID suelen ser cortas, 50 es seguro

        builder.Property(e => e.DevicePassword)
            .IsRequired(false)
            .HasMaxLength(20);

        builder.Property(e => e.FaceTemplate)
            .IsRequired(false)
            .HasMaxLength(200000); // Templates de rostro son grandes (decenas de KB)

        builder.Property(e => e.Photo)
            .IsRequired(false)
            .HasMaxLength(1000000); // Fotografías en Base64 pueden ser pesadas

        // Configuración de huellas
        builder.OwnsMany(e => e.Fingerprints, fb =>
        {
            fb.ToTable("EmployeeFingerprints");
            fb.WithOwner().HasForeignKey("EmployeeId");
            fb.HasKey("Id"); // Shadow PK si hereda de Entity<int>
            fb.Property(p => p.FingerIndex).IsRequired();
            fb.Property(p => p.Template).IsRequired().HasMaxLength(16000); // VX10 templates ~1-2KB base64
        });
    }
}
