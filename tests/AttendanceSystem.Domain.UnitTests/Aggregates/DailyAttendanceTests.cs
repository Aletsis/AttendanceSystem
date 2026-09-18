using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class DailyAttendanceTests
{
    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-001");
    private readonly DateTime _date = new(2026, 8, 28);
    private readonly Shift _standardShift = Shift.Create(
        name: "Turno Matutino",
        startTime: new TimeSpan(8, 0, 0),
        toleranceMinutes: 10,
        workHours: new TimeSpan(8, 0, 0),
        shiftType: ShiftType.Matutino,
        lunchBreakMinutes: 60);

    [Fact]
    public void Create_WhenNoCheckInAndNoCheckOut_ShouldBeMarkedAsAbsent()
    {
        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: null,
            checkOut: null,
            isRestDay: false);

        // Assert
        da.IsAbsent.Should().BeTrue();
        da.LateMinutes.Should().Be(0);
        da.MissingCheckIn.Should().BeFalse();
        da.MissingCheckOut.Should().BeFalse();
        da.WorkedOnRestDay.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenRestDayAndNoPunches_ShouldNotBeAbsent()
    {
        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: null,
            checkOut: null,
            isRestDay: true);

        // Assert
        da.IsAbsent.Should().BeFalse();
        da.IsRestDay.Should().BeTrue();
        da.WorkedOnRestDay.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenPunchedOnRestDay_ShouldBeMarkedAsWorkedOnRestDay()
    {
        // Arrange
        var checkIn = _date.AddHours(9);
        var checkOut = _date.AddHours(14);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: true);

        // Assert
        da.IsRestDay.Should().BeTrue();
        da.WorkedOnRestDay.Should().BeTrue();
        da.IsAbsent.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenCheckInWithinTolerance_ShouldHaveZeroLateMinutes()
    {
        // Arrange (8:08 is within 10 min tolerance for 8:00 start)
        var checkIn = _date.AddHours(8).AddMinutes(8);
        var checkOut = _date.AddHours(17);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert
        da.LateMinutes.Should().Be(0);
        da.IsAbsent.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenCheckInExceedsTolerance_ShouldCalculateLateMinutes()
    {
        // Arrange (8:25 is 25 min late, which exceeds 10 min tolerance)
        var checkIn = _date.AddHours(8).AddMinutes(25);
        var checkOut = _date.AddHours(17);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert
        da.LateMinutes.Should().Be(25);
    }

    [Fact]
    public void Create_WhenLeavesBeforeScheduledCheckOut_ShouldCalculateEarlyDepartureMinutes()
    {
        // Arrange (Scheduled exit is 16:00, leaves at 15:30 -> 30 min early departure)
        var checkIn = _date.AddHours(8);
        var checkOut = _date.AddHours(15).AddMinutes(30);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert
        da.EarlyDepartureMinutes.Should().Be(30);
    }

    [Fact]
    public void ApplyIntermediateAnalysis_WhenUnpaidPermission_ShouldDeductFromWorkedTime()
    {
        // Arrange (8:00 to 18:00 = 10 hrs = 600 min, Scheduled = 8 hrs = 480 min)
        var checkIn = _date.AddHours(8);
        var checkOut = _date.AddHours(18);

        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false,
            overtimeAuthorized: true);

        // Act (Apply intermediate analysis: 60m lunch, 30m temporary exit)
        da.ApplyIntermediateAnalysis(lunchMinutesDeducted: 60, hasTemporaryExits: true, temporaryExitMinutes: 30);
        da.ClassifyTemporaryExit(TemporaryExitStatus.ApprovedUnpaid, "Supervisor");

        // Assert
        // Total worked = 600 - 60 (lunch) - 30 (unpaid) = 510 min.
        // Overtime = 510 - 480 (scheduled) = 30 min.
        da.OvertimeMinutes.Should().Be(30);
        da.TemporaryExitStatus.Should().Be(TemporaryExitStatus.ApprovedUnpaid);
        da.AttendanceNote.Should().Contain("Permiso sin goce");
    }

    [Fact]
    public void Create_WhenWorkingNormalScheduleOnRestDay_ShouldNotHaveOvertime()
    {
        // Arrange (8:00 to 16:00 = 8 hrs = 480 min, scheduled = 8 hrs = 480 min)
        var checkIn = _date.AddHours(8);
        var checkOut = _date.AddHours(16);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: true,
            overtimeAuthorized: true);

        // Assert
        da.WorkedOnRestDay.Should().BeTrue();
        da.IsRestDay.Should().BeTrue();
        da.OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public void Create_WhenWorkingOvertimeOnRestDayWithAuthorization_ShouldCalculateOvertime()
    {
        // Arrange (8:00 to 18:00 = 10 hrs = 600 min, scheduled = 8 hrs = 480 min -> 120 min OT)
        var checkIn = _date.AddHours(8);
        var checkOut = _date.AddHours(18);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: true,
            overtimeAuthorized: true);

        // Assert
        da.WorkedOnRestDay.Should().BeTrue();
        da.IsRestDay.Should().BeTrue();
        da.OvertimeMinutes.Should().Be(120);
    }

    [Fact]
    public void Create_WhenWorkingOvertimeOnRestDayWithoutAuthorization_ShouldHaveZeroOvertime()
    {
        // Arrange (8:00 to 18:00 = 10 hrs = 600 min, scheduled = 8 hrs = 480 min)
        var checkIn = _date.AddHours(8);
        var checkOut = _date.AddHours(18);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: _standardShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: true,
            overtimeAuthorized: false);

        // Assert
        da.WorkedOnRestDay.Should().BeTrue();
        da.IsRestDay.Should().BeTrue();
        da.OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public void Create_WhenWorkingOnRestDayWithoutShift_ShouldUse8HourGoal()
    {
        // Arrange: No shift assigned, worked 9 hours (540 min) -> 60 min OT over 8h (480 min) base
        var checkIn = _date.AddHours(8);
        var checkOut = _date.AddHours(17);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: null,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: true,
            overtimeAuthorized: true);

        // Assert
        da.WorkedOnRestDay.Should().BeTrue();
        da.IsRestDay.Should().BeTrue();
        da.OvertimeMinutes.Should().Be(60);
    }
}
