using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;
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

public class RegisterManualAttendanceCommandHandlerTests
{
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock;
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly RegisterManualAttendanceCommandHandler _handler;

    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-001");
    private readonly DateTime _date = new(2026, 9, 17, 8, 0, 0);

    public RegisterManualAttendanceCommandHandlerTests()
    {
        _attendanceRepoMock = new Mock<IAttendanceRepository>();
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _handler = new RegisterManualAttendanceCommandHandler(
            _attendanceRepoMock.Object,
            _dailyRepoMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task Handle_WhenNoDailyAttendanceExists_ShouldCreateRecordAndCommit()
    {
        // Arrange
        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: _date,
            Type: "Entrada");

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyAttendance?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _attendanceRepoMock.Verify(r => r.AddAsync(It.Is<AttendanceRecord>(rec =>
            rec.EmployeeId == _employeeId &&
            rec.CheckType == CheckType.CheckIn &&
            rec.VerifyMethod == VerifyMethod.Manual), It.IsAny<CancellationToken>()), Times.Once);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCheckInAlreadyExists_ShouldReturnFailure()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var existingDaily = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date.Date,
            shift: shift,
            checkIn: _date,
            checkOut: null,
            isRestDay: false);

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDaily);

        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: _date.AddMinutes(5),
            Type: "Entrada");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Ya existe una Entrada registrada");
        _attendanceRepoMock.Verify(r => r.AddAsync(It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCheckOutAlreadyExists_ShouldReturnFailure()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var existingDaily = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date.Date,
            shift: shift,
            checkIn: _date,
            checkOut: _date.AddHours(8),
            isRestDay: false);

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDaily);

        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: _date.AddHours(8).AddMinutes(10),
            Type: "Salida");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Ya existe una Salida registrada");
        _attendanceRepoMock.Verify(r => r.AddAsync(It.IsAny<AttendanceRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenValidCheckOutForExistingCheckIn_ShouldUpdateDailyAttendance()
    {
        // Arrange
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var existingDaily = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date.Date,
            shift: shift,
            checkIn: _date,
            checkOut: null,
            isRestDay: false);

        _dailyRepoMock
            .Setup(r => r.GetByEmployeeAndDateAsync(_employeeId, _date.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDaily);

        var checkOutTime = _date.AddHours(8);
        var command = new RegisterManualAttendanceCommand(
            EmployeeId: "EMP-001",
            CheckTime: checkOutTime,
            Type: "Salida");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingDaily.ActualCheckOut.Should().Be(checkOutTime);
        existingDaily.MissingCheckOut.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
