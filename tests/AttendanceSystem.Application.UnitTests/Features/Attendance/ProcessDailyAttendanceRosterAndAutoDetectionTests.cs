using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class ProcessDailyAttendanceRosterAndAutoDetectionTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();
    private readonly Mock<IShiftRosterRepository> _rosterRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ISender> _senderMock = new();
    private readonly Mock<ILogger<ProcessDailyAttendanceCommandHandler>> _loggerMock = new();

    private readonly Shift _morningShift;
    private readonly Shift _afternoonShift;
    private readonly Shift _nightShift;
    private readonly DateTime _date = new(2026, 10, 5);

    public ProcessDailyAttendanceRosterAndAutoDetectionTests()
    {
        _morningShift = Shift.Create("Mañana 6-14", new TimeSpan(6, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        _afternoonShift = Shift.Create("Tarde 14-22", new TimeSpan(14, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Vespertino);
        _nightShift = Shift.Create("Noche 22-6", new TimeSpan(22, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Nocturno);

        _shiftRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { _morningShift, _afternoonShift, _nightShift });

        _dailyRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance>());
    }

    [Fact]
    public async Task Handle_WhenRosterExistsForEmployee_ShouldUseRosterShiftInsteadOfDefaultSchedule()
    {
        // Arrange
        var emp = Employee.Create(
            id: EmployeeId.From("EMP-001"),
            firstName: "Carlos",
            lastName: "Gomez",
            email: "carlos@emp.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _morningShift.Id); // Default is Morning

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { emp });

        // But Roster assigns Afternoon shift for Oct 5
        var roster = ShiftRoster.Create(emp.Id, _date, _afternoonShift.Id, isRestDay: false, "Rotación semanal");
        _rosterRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster> { roster });

        _attendanceRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        var handler = new ProcessDailyAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _employeeRepoMock.Object,
            _shiftRepoMock.Object,
            _rosterRepoMock.Object,
            _unitOfWorkMock.Object,
            _senderMock.Object,
            _loggerMock.Object);

        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: emp.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        // Sender should receive ProcessRegularAttendanceCommand with the Afternoon shift from the roster!
        _senderMock.Verify(s => s.Send(It.Is<ProcessRegularAttendanceCommand>(c => c.Shift.Id == _afternoonShift.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmployeeHasRotativeShiftAndNoRoster_ShouldAutoDetectShiftByPunchProximity()
    {
        // Arrange: Employee with ShiftType.Rotativo and no static schedule
        var emp = Employee.Create(
            id: EmployeeId.From("EMP-002"),
            firstName: "Lucia",
            lastName: "Torres",
            email: "lucia@emp.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Female,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Rotativo,
            scheduleId: null);

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { emp });

        _rosterRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        // Employee punches at 13:55 (closest to Afternoon shift at 14:00)
        var punch = AttendanceRecord.Create(emp.Id, DeviceId.From("DEV-01"), _date.AddHours(13).AddMinutes(55), VerifyMethod.Fingerprint, CheckType.CheckIn);
        _attendanceRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { punch });

        var handler = new ProcessDailyAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _employeeRepoMock.Object,
            _shiftRepoMock.Object,
            _rosterRepoMock.Object,
            _unitOfWorkMock.Object,
            _senderMock.Object,
            _loggerMock.Object);

        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: emp.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(1);
        // Sender should receive ProcessRegularAttendanceCommand with the Afternoon shift auto-detected and IsAutoDetectedShift == true!
        _senderMock.Verify(s => s.Send(It.Is<ProcessRegularAttendanceCommand>(c => c.Shift.Id == _afternoonShift.Id && c.IsAutoDetectedShift == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmployeeHasMultipleRestDaysOnFixedShift_ShouldSetIsRestDayForBothDays()
    {
        // Arrange: Saturday (Oct 10) and Sunday (Oct 11)
        var saturday = new DateTime(2026, 10, 10);
        var sunday = new DateTime(2026, 10, 11);

        var emp = Employee.Create(
            id: EmployeeId.From("EMP-003"),
            firstName: "Pedro",
            lastName: "Ramirez",
            email: "pedro@emp.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _morningShift.Id,
            restDays: new[] { WeekDay.Sabado, WeekDay.Domingo });

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { emp });

        _rosterRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        _attendanceRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        var handler = new ProcessDailyAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _employeeRepoMock.Object,
            _shiftRepoMock.Object,
            _rosterRepoMock.Object,
            _unitOfWorkMock.Object,
            _senderMock.Object,
            _loggerMock.Object);

        // Act: Process for both Saturday and Sunday
        var command = new ProcessDailyAttendanceCommand(saturday, sunday, EmployeeId: emp.Id);
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(2);

        // Both Saturday and Sunday should have been sent to ProcessRegularAttendanceCommand with IsRestDay == true
        _senderMock.Verify(s => s.Send(It.Is<ProcessRegularAttendanceCommand>(c => c.Date.Date == saturday.Date && c.IsRestDay == true), It.IsAny<CancellationToken>()), Times.Once);
        _senderMock.Verify(s => s.Send(It.Is<ProcessRegularAttendanceCommand>(c => c.Date.Date == sunday.Date && c.IsRestDay == true), It.IsAny<CancellationToken>()), Times.Once);
    }
}
