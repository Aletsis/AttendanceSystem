using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class ProcessSplitAttendanceCommandHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock;
    private readonly Mock<ILogger<ProcessSplitAttendanceCommandHandler>> _loggerMock;
    private readonly ProcessSplitAttendanceCommandHandler _handler;

    private readonly Employee _employee;
    private readonly DeviceId _deviceId = DeviceId.From("DEV-01");
    private readonly DateTime _date = new(2026, 8, 28);

    public ProcessSplitAttendanceCommandHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _attendanceRepoMock = new Mock<IAttendanceRepository>();
        _loggerMock = new Mock<ILogger<ProcessSplitAttendanceCommandHandler>>();

        _handler = new ProcessSplitAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _loggerMock.Object);

        _employee = Employee.Create(
            id: EmployeeId.From("EMP-001"),
            firstName: "Carlos",
            lastName: "Gomez",
            email: "carlos@empresa.com",
            phoneNumber: null,
            hireDate: new DateTime(2024, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Partido);
    }

    [Fact]
    public async Task Handle_With4Punches_ShouldDistributePunchesAcrossBlocksAndCalculateStatus()
    {
        // Arrange: Block 1: 09:00 - 13:00, Block 2: 17:00 - 21:00
        var splitShift = Shift.Create(
            name: "Partido 9-13 / 17-21",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0),
            secondBlockToleranceMinutes: 10);

        var punch1 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(8).AddMinutes(58), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var punch2 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(13).AddMinutes(2), VerifyMethod.Fingerprint, CheckType.CheckOut);
        var punch3 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(17).AddMinutes(5), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var punch4 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(21).AddMinutes(1), VerifyMethod.Fingerprint, CheckType.CheckOut);

        DailyAttendance? savedDa = null;
        _dailyRepoMock.Setup(r => r.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(da => savedDa = da);

        var command = new ProcessSplitAttendanceCommand(
            _employee,
            _date,
            splitShift,
            new List<AttendanceRecord> { punch1, punch2, punch3, punch4 },
            IsRestDay: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        savedDa.Should().NotBeNull();
        savedDa!.ActualCheckIn.Should().Be(punch1.CheckTime);
        savedDa.ActualBlock1CheckOut.Should().Be(punch2.CheckTime);
        savedDa.ActualBlock2CheckIn.Should().Be(punch3.CheckTime);
        savedDa.ActualCheckOut.Should().Be(punch4.CheckTime);
        savedDa.LateMinutes.Should().Be(0);
        savedDa.EarlyDepartureMinutes.Should().Be(0);
        savedDa.MissingCheckIn.Should().BeFalse();
        savedDa.MissingBlock1CheckOut.Should().BeFalse();
        savedDa.MissingBlock2CheckIn.Should().BeFalse();
        savedDa.MissingCheckOut.Should().BeFalse();

        punch1.Status.Should().Be(AttendanceStatus.Processed);
        punch2.Status.Should().Be(AttendanceStatus.Processed);
        punch3.Status.Should().Be(AttendanceStatus.Processed);
        punch4.Status.Should().Be(AttendanceStatus.Processed);
    }

    [Fact]
    public async Task Handle_WithOnlyCheckInAndCheckOut_ShouldMarkMissingIntermediateBlocks()
    {
        // Arrange
        var splitShift = Shift.Create(
            name: "Partido 9-13 / 17-21",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0));

        // Punched at 09:00 and 21:00 only (midpoint is 15:00)
        var punch1 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(9), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var punch4 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(21), VerifyMethod.Fingerprint, CheckType.CheckOut);

        DailyAttendance? savedDa = null;
        _dailyRepoMock.Setup(r => r.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(da => savedDa = da);

        var command = new ProcessSplitAttendanceCommand(
            _employee,
            _date,
            splitShift,
            new List<AttendanceRecord> { punch1, punch4 },
            IsRestDay: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        savedDa.Should().NotBeNull();
        savedDa!.ActualCheckIn.Should().Be(punch1.CheckTime);
        savedDa.ActualBlock1CheckOut.Should().BeNull();
        savedDa.ActualBlock2CheckIn.Should().BeNull();
        savedDa.ActualCheckOut.Should().Be(punch4.CheckTime);
        savedDa.MissingBlock1CheckOut.Should().BeTrue();
        savedDa.MissingBlock2CheckIn.Should().BeTrue();
    }
}
