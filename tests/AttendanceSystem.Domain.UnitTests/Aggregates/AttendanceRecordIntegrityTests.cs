using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class AttendanceRecordIntegrityTests
{
    [Fact]
    public void Create_WithFullAuditMetadata_ShouldPreserveOriginalValues()
    {
        // Arrange
        var empId = EmployeeId.From("EMP-001");
        var devId = DeviceId.From("DEV-01");
        var originalCivil = new DateTime(2026, 10, 17, 8, 15, 0, DateTimeKind.Unspecified);
        var utcCheckTime = new DateTime(2026, 10, 17, 14, 15, 0, DateTimeKind.Utc);
        var workDate = new DateOnly(2026, 10, 17);

        // Act
        var record = AttendanceRecord.Create(
            empId,
            devId,
            utcCheckTime,
            VerifyMethod.Fingerprint,
            CheckType.CheckIn,
            workDate: workDate,
            originalCheckTime: originalCivil,
            timeZoneId: "America/Mexico_City",
            source: "ADMS");

        // Assert
        record.CheckTime.Should().Be(utcCheckTime);
        record.OriginalCheckTime.Should().Be(originalCivil);
        record.WorkDate.Should().Be(workDate);
        record.TimeZoneId.Should().Be("America/Mexico_City");
        record.Source.Should().Be("ADMS");
        record.CheckType.Should().Be(CheckType.CheckIn);
        record.OriginalCheckType.Should().Be(CheckType.CheckIn);
    }

    [Fact]
    public void SetInferredType_ShouldChangeCheckType_WithoutAlteringOriginalCheckType()
    {
        // Arrange
        var record = AttendanceRecord.Create(
            EmployeeId.From("EMP-001"),
            DeviceId.From("DEV-01"),
            DateTime.UtcNow,
            VerifyMethod.RFIDCard,
            CheckType.CheckIn);

        // Act: El motor de cálculo infiere que en realidad fue salida
        record.SetInferredType(CheckType.CheckOut);

        // Assert: CheckType cambia pero OriginalCheckType permanece inmutable
        record.CheckType.Should().Be(CheckType.CheckOut);
        record.OriginalCheckType.Should().Be(CheckType.CheckIn);
    }

    [Fact]
    public void DeduplicationService_ShouldPreserveLegitimateClosePunches_WithDifferentSeconds()
    {
        // Arrange: Empleado checa dos veces seguidas con 5 segundos de diferencia
        var service = new AttendanceDeduplicationService();
        var empId = EmployeeId.From("EMP-001");
        var devId = DeviceId.From("DEV-01");
        var time1 = new DateTime(2026, 10, 17, 8, 0, 1, DateTimeKind.Utc);
        var time2 = new DateTime(2026, 10, 17, 8, 0, 6, DateTimeKind.Utc);

        var punch1 = AttendanceRecord.Create(empId, devId, time1, VerifyMethod.Fingerprint, CheckType.CheckIn);
        var punch2 = AttendanceRecord.Create(empId, devId, time2, VerifyMethod.Fingerprint, CheckType.CheckIn);

        var candidates = new[] { punch1, punch2 };

        // Act
        var filtered = service.FilterNewRecords(candidates, Array.Empty<AttendanceRecord>());

        // Assert: Ambos registros legítimos deben conservarse
        filtered.Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicationService_ShouldFilterTechnicalDuplicates_WithIdenticalTimestamp()
    {
        // Arrange: Mismo empleado y misma fecha/hora exacta en UTC
        var service = new AttendanceDeduplicationService();
        var empId = EmployeeId.From("EMP-001");
        var devId = DeviceId.From("DEV-01");
        var exactTime = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

        var punchOriginal = AttendanceRecord.Create(empId, devId, exactTime, VerifyMethod.Fingerprint, CheckType.CheckIn);
        var punchDuplicate = AttendanceRecord.Create(empId, devId, exactTime, VerifyMethod.Fingerprint, CheckType.CheckIn);

        // Act
        var filtered = service.FilterNewRecords(new[] { punchDuplicate }, new[] { punchOriginal });

        // Assert: El duplicado técnico debe ser filtrado
        filtered.Should().BeEmpty();
    }
}
