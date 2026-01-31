using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => DeviceId.From(value))
            .HasMaxLength(50)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.IpAddress)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Port)
            .IsRequired();

        builder.Property(x => x.Location)
            .HasMaxLength(200);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.ShouldClearAfterDownload)
            .IsRequired();

        builder.Property(x => x.DownloadMethod)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastDownloadAt);

        builder.Property(x => x.TotalDownloadCount)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion(
                s => s.Id,
                value => DeviceStatus.FromValue(value))
            .HasColumnName("StatusId")
            .HasColumnType("int")
            .IsRequired();

        builder.OwnsOne(x => x.HardwareInfo, hardware =>
        {
            hardware.Property(p => p.SerialNumber).HasColumnName("SerialNumber").HasMaxLength(100);
            hardware.Property(p => p.FirmwareVersion).HasColumnName("FirmwareVersion").HasMaxLength(100);
            hardware.Property(p => p.Platform).HasColumnName("Platform").HasMaxLength(100);
            
            hardware.Property(p => p.UserCount).HasColumnName("UserCount");
            hardware.Property(p => p.FingerprintCount).HasColumnName("FingerprintCount");
            hardware.Property(p => p.FaceCount).HasColumnName("FaceCount");
            hardware.Property(p => p.AttendanceRecordCount).HasColumnName("AttendanceRecordCount");
            
            hardware.Property(p => p.UserCapacity).HasColumnName("UserCapacity");
            hardware.Property(p => p.FingerprintCapacity).HasColumnName("FingerprintCapacity");
            hardware.Property(p => p.FaceCapacity).HasColumnName("FaceCapacity");
            hardware.Property(p => p.AttendanceRecordCapacity).HasColumnName("AttendanceRecordCapacity");
        });

        // Ignorar eventos de dominio (no se persisten)
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.IpAddress).IsUnique();
    }
}
