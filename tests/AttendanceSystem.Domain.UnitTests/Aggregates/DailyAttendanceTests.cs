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

    [Fact]
    public void Create_WhenCheckInHasSecondsWithinToleranceMinute_ShouldBeConsideredOnTime()
    {
        // Arrange: Shift 10:00 to 18:00 (8h), tolerance 5 mins, lunch 0 mins.
        var shift = Shift.Create(
            name: "Matutino 10-18",
            startTime: new TimeSpan(10, 0, 0),
            toleranceMinutes: 5,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino,
            lunchBreakMinutes: 0);

        // CheckIn: 10:05:35 (within the 5th minute of tolerance), CheckOut: 19:20:00
        var checkIn = _date.Add(new TimeSpan(10, 5, 35));
        var checkOut = _date.Add(new TimeSpan(19, 20, 0));

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: shift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false,
            overtimeAuthorized: true);

        // Assert
        da.LateMinutes.Should().Be(0);
        da.GetReferenceEntry().Should().Be(_date.Add(new TimeSpan(10, 0, 0)));
        // Scheduled: 480 min. Reference duration: 19:20 - 10:00 = 560 min. Overtime = 80 min.
        da.OvertimeMinutes.Should().Be(80);
    }

    [Fact]
    public void Create_WhenCheckInExceedsToleranceMinute_ShouldPenalizeToNextHalfHourBlock()
    {
        // Arrange: Shift 10:00 to 18:00 (8h), tolerance 5 mins, lunch 0 mins.
        var shift = Shift.Create(
            name: "Matutino 10-18",
            startTime: new TimeSpan(10, 0, 0),
            toleranceMinutes: 5,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino,
            lunchBreakMinutes: 0);

        // CheckIn: 10:06:15 (minute 6, past 5m tolerance), CheckOut: 19:20:00
        var checkIn = _date.Add(new TimeSpan(10, 6, 15));
        var checkOut = _date.Add(new TimeSpan(19, 20, 0));

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: shift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false,
            overtimeAuthorized: true);

        // Assert
        da.LateMinutes.Should().Be(6);
        da.GetReferenceEntry().Should().Be(_date.Add(new TimeSpan(10, 30, 0)));
        // Reference duration: 19:20 - 10:30 = 530 min. Overtime = 530 - 480 = 50 min.
        da.OvertimeMinutes.Should().Be(50);
    }

    [Fact]
    public void Create_FlexibleShift_ArrivalWithinWindow_ShouldHaveZeroLateMinutesAndDynamicScheduledCheckOut()
    {
        // Arrange: Ventana 07:30 a 09:30, 8 horas objetivo, 30 min comida
        var flexShift = Shift.Create(
            name: "Flexible 7:30-9:30",
            startTime: new TimeSpan(7, 30, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            lunchBreakMinutes: 30,
            flexWindowEndTime: new TimeSpan(9, 30, 0));

        // Llega a las 08:15 (dentro de la ventana) y sale a las 16:45 (8h trabajo + 30m comida = 8h30m)
        var checkIn = _date.Add(new TimeSpan(8, 15, 0));
        var checkOut = _date.Add(new TimeSpan(16, 45, 0));

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert
        da.LateMinutes.Should().Be(0);
        da.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(16, 15, 0))); // Sin comida formal aplicada aún
        da.EarlyDepartureMinutes.Should().Be(0);
        da.OvertimeMinutes.Should().Be(30); // 16:45 - 8:15 = 510 min. 510 - 480 = 30 min
    }

    [Fact]
    public void Create_FlexibleShift_ArrivalAfterWindowWithinTolerance_ShouldHaveZeroLateMinutes()
    {
        // Arrange: Ventana hasta 09:30, tolerancia 10 min. Llega a las 09:38.
        var flexShift = Shift.Create(
            name: "Flexible 7:30-9:30",
            startTime: new TimeSpan(7, 30, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            flexWindowEndTime: new TimeSpan(9, 30, 0));

        var checkIn = _date.Add(new TimeSpan(9, 38, 0));
        var checkOut = _date.Add(new TimeSpan(18, 0, 0));

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert: 9:38 es después de 9:30 por 8 min, pero <= 10 min tolerancia -> no genera retardo
        da.LateMinutes.Should().Be(0);
        da.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(17, 38, 0)));
    }

    [Fact]
    public void Create_FlexibleShift_ArrivalAfterWindowExceedingTolerance_ShouldGenerateLateMinutes()
    {
        // Arrange: Ventana hasta 09:30, tolerancia 10 min. Llega a las 09:45.
        var flexShift = Shift.Create(
            name: "Flexible 7:30-9:30",
            startTime: new TimeSpan(7, 30, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            flexWindowEndTime: new TimeSpan(9, 30, 0));

        var checkIn = _date.Add(new TimeSpan(9, 45, 0));
        var checkOut = _date.Add(new TimeSpan(18, 0, 0));

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert: 9:45 - 9:30 = 15 min > 10 min -> Retardo de 15 minutos
        da.LateMinutes.Should().Be(15);
        da.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(17, 45, 0)));
    }

    [Fact]
    public void Create_FlexibleShift_DynamicCheckOut_EarlyDepartureAndOvertimeCalculation()
    {
        // Arrange: Entró 08:30, jornada objetivo 8h -> salida esperada 16:30.
        var flexShift = Shift.Create(
            name: "Flexible 8:00-9:30",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            flexWindowEndTime: new TimeSpan(9, 30, 0));

        // Sale a las 16:10 -> 20 minutos de salida anticipada
        var checkIn = _date.Add(new TimeSpan(8, 30, 0));
        var checkOutEarly = _date.Add(new TimeSpan(16, 10, 0));

        // Act
        var daEarly = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn,
            checkOut: checkOutEarly,
            isRestDay: false);

        // Assert
        daEarly.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(16, 30, 0)));
        daEarly.EarlyDepartureMinutes.Should().Be(20);
        daEarly.OvertimeMinutes.Should().Be(0);

        // Sale a las 17:30 -> tiempo extra de 60 minutos
        var checkOutOvertime = _date.Add(new TimeSpan(17, 30, 0));
        var daOvertime = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn,
            checkOut: checkOutOvertime,
            isRestDay: false,
            overtimeAuthorized: true);

        daOvertime.EarlyDepartureMinutes.Should().Be(0);
        daOvertime.OvertimeMinutes.Should().Be(60);
    }

    [Fact]
    public void Create_FlexibleShift_WithRoundingEnabled_ShouldRoundReferenceEntry()
    {
        // Arrange: Redondeo de 15 minutos, tolerancia 5 min
        var flexShift = Shift.Create(
            name: "Flexible Con Redondeo",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 5,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            roundingsEnabled: true,
            roundingInterval: 15,
            flexWindowEndTime: new TimeSpan(9, 30, 0));

        // Llega 08:04 -> redondea a 08:00
        var checkIn1 = _date.Add(new TimeSpan(8, 4, 0));
        var da1 = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn1,
            checkOut: _date.Add(new TimeSpan(16, 0, 0)));

        da1.GetReferenceEntry().Should().Be(_date.Add(new TimeSpan(8, 0, 0)));
        da1.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(16, 0, 0)));

        // Llega 08:08 -> redondea a 08:15
        var checkIn2 = _date.Add(new TimeSpan(8, 8, 0));
        var da2 = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn2,
            checkOut: _date.Add(new TimeSpan(16, 15, 0)));

        da2.GetReferenceEntry().Should().Be(_date.Add(new TimeSpan(8, 15, 0)));
        da2.DynamicScheduledCheckOut.Should().Be(_date.Add(new TimeSpan(16, 15, 0)));
    }

    [Fact]
    public void Create_FlexibleShift_WithWeeklyHours_ShouldNotHaveDynamicCheckOutNorDailyEarlyDeparture()
    {
        // Arrange
        var flexShift = Shift.Create(
            name: "Flexible Bolsa Semanal",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            flexWindowEndTime: new TimeSpan(10, 0, 0),
            weeklyWorkHours: new TimeSpan(40, 0, 0));

        // Llega 08:45, sale 14:00 (5h 15m trabajadas)
        var checkIn = _date.Add(new TimeSpan(8, 45, 0));
        var checkOut = _date.Add(new TimeSpan(14, 0, 0));

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: flexShift,
            checkIn: checkIn,
            checkOut: checkOut,
            isRestDay: false);

        // Assert
        da.WeeklyWorkHours.Should().Be(TimeSpan.FromHours(40));
        da.WorkHours.Should().BeNull();
        da.LateMinutes.Should().Be(0); // Dentro de ventana 8:00 - 10:00
        da.DynamicScheduledCheckOut.Should().BeNull(); // Sin salida fija diaria
        da.EarlyDepartureMinutes.Should().Be(0); // Sin salida temprana diaria
        da.OvertimeMinutes.Should().Be(0); // Las horas se acumulan a la bolsa semanal
    }

    [Fact]
    public void Create_SplitShift_WithAll4PunchesOnTime_ShouldHaveNoTardinessNorEarlyDeparture()
    {
        // Arrange: Block 1: 09:00 - 13:00 (dur 4h, Tol 10m), Block 2: 17:00 - 21:00 (dur 4h, Tol 15m)
        var splitShift = Shift.Create(
            name: "Horario Partido Comercial",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0),
            secondBlockToleranceMinutes: 15);

        var punchB1In = _date.AddHours(9).AddMinutes(5);
        var punchB1Out = _date.AddHours(13).AddMinutes(2);
        var punchB2In = _date.AddHours(17).AddMinutes(12);
        var punchB2Out = _date.AddHours(21).AddMinutes(5);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: splitShift,
            checkIn: punchB1In,
            checkOut: punchB2Out,
            isRestDay: false);

        da.SetBlock1CheckOut(punchB1Out, AttendanceRecordId.CreateNew());
        da.SetBlock2CheckIn(punchB2In, AttendanceRecordId.CreateNew());

        // Assert
        da.IsAbsent.Should().BeFalse();
        da.MissingCheckIn.Should().BeFalse();
        da.MissingBlock1CheckOut.Should().BeFalse();
        da.MissingBlock2CheckIn.Should().BeFalse();
        da.MissingCheckOut.Should().BeFalse();
        da.LateMinutes.Should().Be(0); // 5 min <= 10 min tol, 12 min <= 15 min tol
        da.EarlyDepartureMinutes.Should().Be(0);
    }

    [Fact]
    public void Create_SplitShift_WithTardinessInBothBlocksIndependentTolerance_ShouldCalculateAccumulatedLateMinutes()
    {
        // Arrange: Block 1 tol: 10m, Block 2 tol: 5m
        var splitShift = Shift.Create(
            name: "Horario Partido Comercial",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0),
            secondBlockToleranceMinutes: 5);

        // Block 1: arrives 9:18 (18m late > 10m tol -> 18m late)
        var punchB1In = _date.AddHours(9).AddMinutes(18);
        var punchB1Out = _date.AddHours(13);
        // Block 2: arrives 17:10 (10m late > 5m tol -> 10m late)
        var punchB2In = _date.AddHours(17).AddMinutes(10);
        var punchB2Out = _date.AddHours(21);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: splitShift,
            checkIn: punchB1In,
            checkOut: punchB2Out,
            isRestDay: false);

        da.SetBlock1CheckOut(punchB1Out, AttendanceRecordId.CreateNew());
        da.SetBlock2CheckIn(punchB2In, AttendanceRecordId.CreateNew());

        // Assert
        da.LateMinutes.Should().Be(28); // 18 + 10 = 28
    }

    [Fact]
    public void Create_SplitShift_WithEarlyDepartureInBothBlocks_ShouldCalculateAccumulatedEarlyDepartureMinutes()
    {
        // Arrange: Block 1 ends 13:00, Block 2 ends 21:00
        var splitShift = Shift.Create(
            name: "Horario Partido Comercial",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0));

        // Block 1: leaves at 12:45 (15 min early)
        var punchB1In = _date.AddHours(9);
        var punchB1Out = _date.AddHours(12).AddMinutes(45);
        // Block 2: leaves at 20:40 (20 min early)
        var punchB2In = _date.AddHours(17);
        var punchB2Out = _date.AddHours(20).AddMinutes(40);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: splitShift,
            checkIn: punchB1In,
            checkOut: punchB2Out,
            isRestDay: false);

        da.SetBlock1CheckOut(punchB1Out, AttendanceRecordId.CreateNew());
        da.SetBlock2CheckIn(punchB2In, AttendanceRecordId.CreateNew());

        // Assert
        da.EarlyDepartureMinutes.Should().Be(35); // 15 + 20 = 35
    }

    [Fact]
    public void Create_SplitShift_WithOvernightSecondBlock_ShouldCalculateAccurateHoursAndTimes()
    {
        // Arrange: Block 1: 12:00 - 16:00 (4h), Block 2: 20:00 - 02:00 (+1 day)
        var splitShift = Shift.Create(
            name: "Horario Partido Nocturno",
            startTime: new TimeSpan(12, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(20, 0, 0),
            secondBlockEndTime: new TimeSpan(2, 0, 0));

        var punchB1In = _date.AddHours(12);
        var punchB1Out = _date.AddHours(16);
        var punchB2In = _date.AddHours(20);
        // Leaves at 01:45 next day -> 15 min early departure
        var punchB2Out = _date.AddDays(1).AddHours(1).AddMinutes(45);

        // Act
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: splitShift,
            checkIn: punchB1In,
            checkOut: punchB2Out,
            isRestDay: false);

        da.SetBlock1CheckOut(punchB1Out, AttendanceRecordId.CreateNew());
        da.SetBlock2CheckIn(punchB2In, AttendanceRecordId.CreateNew());

        // Assert
        da.EarlyDepartureMinutes.Should().Be(15);
        da.LateMinutes.Should().Be(0);
    }

    [Fact]
    public void Create_SplitShift_MissingIntermediatePunches_ShouldFlagMissingPunches()
    {
        // Arrange
        var splitShift = Shift.Create(
            name: "Horario Partido Comercial",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(4, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(17, 0, 0),
            secondBlockEndTime: new TimeSpan(21, 0, 0));

        // Punched check-in and check-out, but no block 1 check-out nor block 2 check-in
        var da = DailyAttendance.Create(
            employeeId: _employeeId,
            date: _date,
            shift: splitShift,
            checkIn: _date.AddHours(9),
            checkOut: _date.AddHours(21),
            isRestDay: false);

        // Assert
        da.MissingCheckIn.Should().BeFalse();
        da.MissingBlock1CheckOut.Should().BeTrue();
        da.MissingBlock2CheckIn.Should().BeTrue();
        da.MissingCheckOut.Should().BeFalse();
    }
}

