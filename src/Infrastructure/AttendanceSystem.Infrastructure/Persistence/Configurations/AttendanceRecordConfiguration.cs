using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("AttendanceRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => AttendanceRecordId.From(value))
            .ValueGeneratedNever();

        builder.Property(x => x.EmployeeId)
            .HasConversion(
                id => id.Value,
                value => EmployeeId.From(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.DeviceId)
            .HasConversion(
                id => id.Value,
                value => DeviceId.From(value))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CheckTime)
            .IsRequired();

        builder.Property(x => x.VerifyMethod)
            .HasConversion(
                vm => vm.Id,
                value => VerifyMethod.FromValue(value))
            .HasColumnName("VerifyMethodId")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(x => x.CheckType)
            .HasConversion(
                ct => ct.Id,
                value => CheckType.FromValue(value))
            .HasColumnName("CheckTypeId")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion(
                s => s.Id,
                value => AttendanceStatus.FromValue(value))
            .HasColumnName("StatusId")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(x => x.DownloadLogId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value,
                value => value == null ? null : DownloadLogId.From(value.Value))
            .IsRequired(false);

        builder.Property(x => x.OriginalCheckTime)
            .IsRequired(false);

        builder.Property(x => x.WorkDate)
            .IsRequired(false);

        builder.Property(x => x.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(x => x.Source)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(x => x.OriginalCheckType)
            .HasConversion(
                ct => ct == null ? (int?)null : ct.Id,
                value => value == null ? null : CheckType.FromValue(value.Value))
            .HasColumnName("OriginalCheckTypeId")
            .HasColumnType("int")
            .IsRequired(false);

        builder.Property(x => x.RegisteredBy)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(x => x.RegisteredAt)
            .IsRequired(false);

        builder.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Ignore(x => x.IsManual);
        builder.Ignore(x => x.IsVoided);

        builder.Property(x => x.VoidedAt).IsRequired(false);
        builder.Property(x => x.VoidedBy).HasMaxLength(256).IsRequired(false);
        builder.Property(x => x.VoidReason).HasMaxLength(500).IsRequired(false);
        builder.Property(x => x.ReplacedByRecordId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value, value => value == null ? null : AttendanceRecordId.From(value.Value))
            .IsRequired(false);
        builder.Property(x => x.ReplacesRecordId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value, value => value == null ? null : AttendanceRecordId.From(value.Value))
            .IsRequired(false);

        // Ignorar eventos de dominio (no se persisten en esta tabla)
        builder.Ignore(x => x.DomainEvents);

        // H09: Restricción única de marcaje para prevenir duplicados técnicos en base de datos
        // Caso 14: única entre los marcajes vigentes; un anulado y su corregido (u otro corregido) pueden compartir instante.
        builder.HasIndex(x => new { x.EmployeeId, x.CheckTime })
            .IsUnique()
            .HasFilter("\"VoidedAt\" IS NULL");

        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate });
        builder.HasIndex(x => x.DeviceId);
    }
}
