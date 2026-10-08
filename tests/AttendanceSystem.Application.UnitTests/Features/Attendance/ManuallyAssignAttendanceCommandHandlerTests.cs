using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class ManuallyAssignAttendanceCommandHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly ManuallyAssignAttendanceCommandHandler _handler;

    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-001");
    private readonly DateTime _date = new(2026, 8, 28);
    private readonly Shift _splitShift;

    public ManuallyAssignAttendanceCommandHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _attendanceRepoMock = new Mock<IAttendanceRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _handler = new ManuallyAssignAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _unitOfWorkMock.Object);

        _splitShift = Shift.Create(
            name: "Partido 9-13 / 17-21",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0));
    }

    [Fact]
    public async Task Handle_WithSalidaB1AndEntradaB2_ShouldAssignBlocksCorrectly()
    {
        // Arrange
        var dailyAttendance = DailyAttendance.Create(
            _employeeId,
            _date,
            _splitShift,
            checkIn: _date.AddHours(9),
            checkOut: _date.AddHours(21),
            isRestDay: false);

        var b1OutRecord = AttendanceRecord.Create(
            _employeeId,
            DeviceId.From("DEV-01"),
            _date.AddHours(13),
            VerifyMethod.Fingerprint,
            CheckType.CheckOut);

        var b2InRecord = AttendanceRecord.Create(
            _employeeId,
            DeviceId.From("DEV-01"),
            _date.AddHours(17),
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);

        _dailyRepoMock.Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dailyAttendance);

        _attendanceRepoMock.Setup(r => r.GetByIdAsync(b1OutRecord.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(b1OutRecord);

        _attendanceRepoMock.Setup(r => r.GetByIdAsync(b2InRecord.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(b2InRecord);

        // Act 1: Asignar Salida B1
        var cmd1 = new ManuallyAssignAttendanceCommand(
            _employeeId.Value,
            DateOnly.FromDateTime(_date),
            b1OutRecord.Id.Value.ToString(),
            "Salida B1");

        var res1 = await _handler.Handle(cmd1, CancellationToken.None);

        // Act 2: Asignar Entrada B2
        var cmd2 = new ManuallyAssignAttendanceCommand(
            _employeeId.Value,
            DateOnly.FromDateTime(_date),
            b2InRecord.Id.Value.ToString(),
            "Entrada B2");

        var res2 = await _handler.Handle(cmd2, CancellationToken.None);

        // Assert
        res1.IsSuccess.Should().BeTrue();
        res2.IsSuccess.Should().BeTrue();
        dailyAttendance.ActualBlock1CheckOut.Should().Be(b1OutRecord.CheckTime);
        dailyAttendance.Block1CheckOutRecordId.Should().Be(b1OutRecord.Id);
        dailyAttendance.ActualBlock2CheckIn.Should().Be(b2InRecord.CheckTime);
        dailyAttendance.Block2CheckInRecordId.Should().Be(b2InRecord.Id);
        b1OutRecord.Status.Should().Be(AttendanceStatus.Processed);
        b2InRecord.Status.Should().Be(AttendanceStatus.Processed);
    }

    [Fact]
    public async Task Handle_WithNoneAssignment_ShouldUnassignAndResetStatusToPending()
    {
        // Arrange
        var dailyAttendance = DailyAttendance.Create(
            _employeeId,
            _date,
            _splitShift,
            checkIn: _date.AddHours(9),
            checkOut: _date.AddHours(21),
            isRestDay: false);

        var b1OutRecord = AttendanceRecord.Create(
            _employeeId,
            DeviceId.From("DEV-01"),
            _date.AddHours(13),
            VerifyMethod.Fingerprint,
            CheckType.CheckOut);
        b1OutRecord.MarkAsProcessed();

        dailyAttendance.SetBlock1CheckOut(b1OutRecord.CheckTime, b1OutRecord.Id);

        _dailyRepoMock.Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dailyAttendance);

        _attendanceRepoMock.Setup(r => r.GetByIdAsync(b1OutRecord.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(b1OutRecord);

        // Act: Desasignar
        var cmd = new ManuallyAssignAttendanceCommand(
            _employeeId.Value,
            DateOnly.FromDateTime(_date),
            b1OutRecord.Id.Value.ToString(),
            "None");

        var res = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        res.IsSuccess.Should().BeTrue();
        dailyAttendance.ActualBlock1CheckOut.Should().BeNull();
        dailyAttendance.Block1CheckOutRecordId.Should().BeNull();
        b1OutRecord.Status.Should().Be(AttendanceStatus.Pending);
    }
}
