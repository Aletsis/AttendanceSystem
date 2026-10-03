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

public class ProcessFlexibleAttendanceCommandHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock;
    private readonly Mock<ILogger<ProcessFlexibleAttendanceCommandHandler>> _loggerMock;
    private readonly ProcessFlexibleAttendanceCommandHandler _handler;

    private readonly Employee _employee;
    private readonly Shift _flexShift;
    private readonly DeviceId _deviceId = DeviceId.From("DEV-01");
    private readonly DateTime _date = new(2026, 8, 28);

    public ProcessFlexibleAttendanceCommandHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _attendanceRepoMock = new Mock<IAttendanceRepository>();
        _loggerMock = new Mock<ILogger<ProcessFlexibleAttendanceCommandHandler>>();

        _handler = new ProcessFlexibleAttendanceCommandHandler(
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
            shiftType: ShiftType.Flexible);

        _flexShift = Shift.Create(
            name: "Flexible 8:00-9:30",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            lunchBreakMinutes: 30,
            flexWindowEndTime: new TimeSpan(9, 30, 0));
    }

    [Fact]
    public async Task Handle_WhenEmployeePunchesWithinArrivalWindow_ShouldProcessCorrectlyWithNoLateness()
    {
        // Arrange: Llegó a las 08:45 (dentro de ventana 8:00-9:30) y salió a las 16:45
        var punchIn = AttendanceRecord.Create(
            _employee.Id, _deviceId, _date.Add(new TimeSpan(8, 45, 0)), VerifyMethod.Fingerprint, CheckType.CheckIn);
        var punchOut = AttendanceRecord.Create(
            _employee.Id, _deviceId, _date.Add(new TimeSpan(16, 45, 0)), VerifyMethod.Fingerprint, CheckType.CheckOut);

        DailyAttendance? savedDa = null;
        _dailyRepoMock.Setup(r => r.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(da => savedDa = da);

        var command = new ProcessFlexibleAttendanceCommand(
            _employee,
            _date,
            _flexShift,
            new List<AttendanceRecord> { punchIn, punchOut },
            IsRestDay: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        savedDa.Should().NotBeNull();
        savedDa!.ActualCheckIn.Should().Be(_date.Add(new TimeSpan(8, 45, 0)));
        savedDa.ActualCheckOut.Should().Be(_date.Add(new TimeSpan(16, 45, 0)));
        savedDa.LateMinutes.Should().Be(0);
        savedDa.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(16, 45, 0)));
        savedDa.EarlyDepartureMinutes.Should().Be(0);

        punchIn.Status.Should().Be(AttendanceStatus.Processed);
        punchOut.Status.Should().Be(AttendanceStatus.Processed);
    }
}
