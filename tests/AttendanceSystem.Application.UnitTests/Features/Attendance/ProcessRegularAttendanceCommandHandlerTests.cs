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

public class ProcessRegularAttendanceCommandHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock;
    private readonly Mock<ILogger<ProcessRegularAttendanceCommandHandler>> _loggerMock;
    private readonly ProcessRegularAttendanceCommandHandler _handler;

    private readonly Employee _employee;
    private readonly Shift _shift;
    private readonly DeviceId _deviceId = DeviceId.From("DEV-01");
    private readonly DateTime _date = new(2026, 8, 28);

    public ProcessRegularAttendanceCommandHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _attendanceRepoMock = new Mock<IAttendanceRepository>();
        _loggerMock = new Mock<ILogger<ProcessRegularAttendanceCommandHandler>>();

        _handler = new ProcessRegularAttendanceCommandHandler(
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
            shiftType: ShiftType.Matutino);

        _shift = Shift.Create(
            name: "Matutino 8-16",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino,
            lunchBreakMinutes: 60);
    }

    [Fact]
    public async Task Handle_WhenValidCheckInAndCheckOut_ShouldSaveDailyAttendanceAndMarkRecordsProcessed()
    {
        // Arrange
        var rec1 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(8), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var rec2 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(16), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var records = new List<AttendanceRecord> { rec1, rec2 };

        var command = new ProcessRegularAttendanceCommand(_employee, _date, _shift, records, IsRestDay: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        rec1.Status.Should().Be(AttendanceStatus.Processed);
        rec1.CheckType.Should().Be(CheckType.CheckIn);

        rec2.Status.Should().Be(AttendanceStatus.Processed);
        rec2.CheckType.Should().Be(CheckType.CheckOut);

        _attendanceRepoMock.Verify(r => r.UpdateAsync(rec1, It.IsAny<CancellationToken>()), Times.Once);
        _attendanceRepoMock.Verify(r => r.UpdateAsync(rec2, It.IsAny<CancellationToken>()), Times.Once);

        _dailyRepoMock.Verify(r => r.Add(It.Is<DailyAttendance>(da =>
            da.EmployeeId == _employee.Id &&
            da.ActualCheckIn == _date.AddHours(8) &&
            da.ActualCheckOut == _date.AddHours(16) &&
            !da.IsAbsent)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDoubleTapWithin15Minutes_ShouldFilterDoubleTapFromIntermediateAnalysis()
    {
        // Arrange
        var recIn1 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(8), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var recIn2 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(8).AddMinutes(2), VerifyMethod.Fingerprint, CheckType.CheckIn); // Double tap
        var recOut = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(16), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var records = new List<AttendanceRecord> { recIn1, recIn2, recOut };

        var command = new ProcessRegularAttendanceCommand(_employee, _date, _shift, records, IsRestDay: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _dailyRepoMock.Verify(r => r.Add(It.Is<DailyAttendance>(da =>
            da.ActualCheckIn == _date.AddHours(8) &&
            da.ActualCheckOut == _date.AddHours(16) &&
            da.HasTemporaryExits == false)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSingleCheckInRecord_ShouldRecordMissingCheckOut()
    {
        // Arrange
        var rec1 = AttendanceRecord.Create(_employee.Id, _deviceId, _date.AddHours(8), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var records = new List<AttendanceRecord> { rec1 };

        var command = new ProcessRegularAttendanceCommand(_employee, _date, _shift, records, IsRestDay: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        rec1.Status.Should().Be(AttendanceStatus.Processed);
        rec1.CheckType.Should().Be(CheckType.CheckIn);

        _dailyRepoMock.Verify(r => r.Add(It.Is<DailyAttendance>(da =>
            da.ActualCheckIn == _date.AddHours(8) &&
            da.ActualCheckOut == null &&
            da.MissingCheckOut == true)), Times.Once);
    }
}
