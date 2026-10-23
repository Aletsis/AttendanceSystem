using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AttendanceAdjustmentConfiguration : IEntityTypeConfiguration<AttendanceAdjustment>
{
    public void Configure(EntityTypeBuilder<AttendanceAdjustment> builder)
    {
        builder.ToTable("AttendanceAdjustments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.IsRestDayOverride);

        builder.Property(x => x.ShiftIdOverride)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? ShiftId.From(value.Value) : null);

        // F05-01: estado por posición (0 sin decisión, 1 asignación forzada, 2 vacío forzado).
        builder.Property(x => x.CheckInOverride).HasConversion<int>().HasDefaultValue(PositionOverrideKind.None).IsRequired();
        builder.Property(x => x.Block1CheckOutOverride).HasConversion<int>().HasDefaultValue(PositionOverrideKind.None).IsRequired();
        builder.Property(x => x.Block2CheckInOverride).HasConversion<int>().HasDefaultValue(PositionOverrideKind.None).IsRequired();
        builder.Property(x => x.CheckOutOverride).HasConversion<int>().HasDefaultValue(PositionOverrideKind.None).IsRequired();
        builder.Property(x => x.Revision).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.CheckInRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.CheckInTimeOverride);

        builder.Property(x => x.CheckOutRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.CheckOutTimeOverride);

        builder.Property(x => x.Block1CheckOutRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.Block1CheckOutTimeOverride);

        builder.Property(x => x.Block2CheckInRecordId)
            .HasConversion(
                id => id != null ? id.Value : (Guid?)null,
                value => value.HasValue ? AttendanceRecordId.From(value.Value) : null);

        builder.Property(x => x.Block2CheckInTimeOverride);

        builder.Property(x => x.TemporaryExitStatus)
            .HasConversion<int?>();

        builder.Property(x => x.TemporaryExitNote)
            .HasMaxLength(500);

        builder.Property(x => x.OvertimeAuthorizedOverride);
        builder.Property(x => x.CalculateOvertimeBeforeEntryOverride);
        builder.Property(x => x.AuthorizedOvertimeMinutes);
        builder.Property(x => x.AuthorizedOvertimeForRawMinutes);

        builder.Property(x => x.LunchExcessForgiven).IsRequired().HasDefaultValue(false);

        // Caso 11: clasificación por salida, como JSON {marcaje de salida: estado}.
        builder.Property(x => x.ExitClassifications)
            .HasColumnType("text")
            .HasConversion(
                map => System.Text.Json.JsonSerializer.Serialize(map, (System.Text.Json.JsonSerializerOptions?)null),
                json => string.IsNullOrEmpty(json)
                    ? new Dictionary<Guid, TemporaryExitStatus>()
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, TemporaryExitStatus>>(json, (System.Text.Json.JsonSerializerOptions?)null)
                      ?? new Dictionary<Guid, TemporaryExitStatus>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<Guid, TemporaryExitStatus>>(
                    (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
                    map => map.Aggregate(0, (hash, entry) => HashCode.Combine(hash, entry.Key, entry.Value)),
                    map => new Dictionary<Guid, TemporaryExitStatus>(map)))
            .IsRequired();

        builder.PrimitiveCollection(x => x.ExcludedPunches).IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(100);

        builder.Property(x => x.UpdatedAt);

        // F03-03: concurrencia optimista con la columna de sistema xmin (no requiere migración). Detecta
        // escrituras sobre datos obsoletos de cualquier escritor que no haya tomado el bloqueo del empleado.
        builder.Property<uint>("Version").IsRowVersion();

        // Índice único: solo un ajuste por empleado y fecha
        builder.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        builder.HasIndex(x => x.Date);
    }
}
