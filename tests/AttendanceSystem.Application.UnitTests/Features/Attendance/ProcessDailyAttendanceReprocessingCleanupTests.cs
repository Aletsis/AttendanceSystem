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

public class ProcessDailyAttendanceReprocessingCleanupTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();
    private readonly Mock<IShiftRosterRepository> _rosterRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ISender> _senderMock = new();
    private readonly Mock<ILogger<ProcessDailyAttendanceCommandHandler>> _loggerMock = new();

    private readonly Shift _splitShift;
    private readonly Employee _employee;
    private readonly DateTime _date = new(2026, 10, 8);

    public ProcessDailyAttendanceReprocessingCleanupTests()
    {
        _splitShift = Shift.Create(
            name: "Partido 9-13 / 17-21",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0));

        _employee = Employee.Create(
            id: EmployeeId.From("EMP-004"),
            firstName: "Alejandro",
            lastName: "Lopez",
            email: "alejandro@emp.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Partido,
            scheduleId: _splitShift.Id);

        _shiftRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { _splitShift });

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _employee });

        _rosterRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());
    }

    [Fact]
    public async Task Handle_WhenRecalculatingRange_ShouldResetAllProcessedAttendanceRecordsToPendingAndRemoveExistingDailyAttendances()
    {
        // Arrange
        // Daily attendance previously generated
        var existingDa = DailyAttendance.Create(
            _employee.Id,
            _date,
            _splitShift,
            checkIn: _date.AddHours(9),
            checkOut: _date.AddHours(21),
            isRestDay: false);

        _dailyRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { existingDa });

        // 4 biometric punches all currently in Processed status from a previous run
        var punch1 = AttendanceRecord.Create(_employee.Id, DeviceId.From("DEV-01"), _date.AddHours(9), VerifyMethod.Fingerprint, CheckType.CheckIn);
        punch1.MarkAsProcessed();

        var punch2 = AttendanceRecord.Create(_employee.Id, DeviceId.From("DEV-01"), _date.AddHours(13), VerifyMethod.Fingerprint, CheckType.CheckOut);
        punch2.MarkAsProcessed();

        var punch3 = AttendanceRecord.Create(_employee.Id, DeviceId.From("DEV-01"), _date.AddHours(17), VerifyMethod.Fingerprint, CheckType.CheckIn);
        punch3.MarkAsProcessed();

        var punch4 = AttendanceRecord.Create(_employee.Id, DeviceId.From("DEV-01"), _date.AddHours(21), VerifyMethod.Fingerprint, CheckType.CheckOut);
        punch4.MarkAsProcessed();

        var allPunches = new List<AttendanceRecord> { punch1, punch2, punch3, punch4 };

        _attendanceRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allPunches);

        var handler = new ProcessDailyAttendanceCommandHandler(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _employeeRepoMock.Object,
            _shiftRepoMock.Object,
            _rosterRepoMock.Object,
            _unitOfWorkMock.Object,
            _senderMock.Object,
            _loggerMock.Object);

        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: _employee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(1);

        // 1. Existing DailyAttendance must have been removed
        _dailyRepoMock.Verify(r => r.Remove(existingDa), Times.Once);

        // 2. All 4 punches in range must have been reset to Pending during pre-cleanup
        // (Before the sub-command gets invoked, punch1 through punch4 were reset to Pending)
        _attendanceRepoMock.Verify(r => r.UpdateAsync(punch1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _attendanceRepoMock.Verify(r => r.UpdateAsync(punch2, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _attendanceRepoMock.Verify(r => r.UpdateAsync(punch3, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _attendanceRepoMock.Verify(r => r.UpdateAsync(punch4, It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // 3. Sender must have received ProcessSplitAttendanceCommand containing all 4 punches now available
        _senderMock.Verify(s => s.Send(
            It.Is<ProcessSplitAttendanceCommand>(c => c.Records.Count == 4),
            It.IsAny<CancellationToken>()), Times.Once);

        // 4. SaveChangesAsync was called at least twice (once for pre-cleanup, once at the end)
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
