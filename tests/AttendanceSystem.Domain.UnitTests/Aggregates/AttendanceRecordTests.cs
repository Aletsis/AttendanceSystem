using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class AttendanceRecordTests
{
    [Fact]
    public void Create_ValidRecord_ShouldHavePendingStatusAndRaiseDomainEvent()
    {
        // Arrange
        var employeeId = EmployeeId.From("EMP-001");
        var deviceId = DeviceId.From("DEV-01");
        var checkTime = new DateTime(2026, 8, 28, 8, 30, 0);

        // Act
        var record = AttendanceRecord.Create(
            employeeId: employeeId,
            deviceId: deviceId,
            checkTime: checkTime,
            verifyMethod: VerifyMethod.Fingerprint,
            checkType: CheckType.CheckIn);

        // Assert
        record.Status.Should().Be(AttendanceStatus.Pending);
        record.EmployeeId.Should().Be(employeeId);
        record.DeviceId.Should().Be(deviceId);
        record.CheckTime.Should().Be(checkTime);
        record.DomainEvents.Should().NotBeEmpty();
    }

    [Fact]
    public void MarkAsProcessed_ShouldChangeStatusToProcessed()
    {
        // Arrange
        var record = AttendanceRecord.Create(
            employeeId: EmployeeId.From("EMP-001"),
            deviceId: DeviceId.From("DEV-01"),
            checkTime: new DateTime(2026, 8, 28, 8, 30, 0),
            verifyMethod: VerifyMethod.Password,
            checkType: CheckType.CheckIn);

        // Act
        record.MarkAsProcessed();

        // Assert
        record.Status.Should().Be(AttendanceStatus.Processed);
    }

    [Fact]
    public void ResetStatus_ShouldRevertStatusToPending()
    {
        // Arrange
        var record = AttendanceRecord.Create(
            employeeId: EmployeeId.From("EMP-001"),
            deviceId: DeviceId.From("DEV-01"),
            checkTime: new DateTime(2026, 8, 28, 8, 30, 0),
            verifyMethod: VerifyMethod.Password,
            checkType: CheckType.CheckIn);
        record.MarkAsProcessed();

        // Act
        record.ResetStatus();

        // Assert
        record.Status.Should().Be(AttendanceStatus.Pending);
    }
}
