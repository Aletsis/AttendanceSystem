using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;

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

        // Ignorar eventos de dominio (no se persisten)
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => new { x.EmployeeId, x.CheckTime });
        builder.HasIndex(x => x.DeviceId);
    }
}
