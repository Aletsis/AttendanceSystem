using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 8 (turno partido 09:00–13:00 y 15:00–19:00, 10 min de tolerancia en ambos bloques): retardo y tiempo
/// trabajado como en turno fijo, extras netas del día, asignación de marcajes por cercanía en lugar del punto medio
/// del descanso y marcajes a mitad de un bloque como en turno fijo.
/// </summary>
public class SplitShiftRulesTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-SPL-008");

    private static AttendanceRecalculationHarness Harness()
    {
        var shift = Shift.Create("Partido 9-13 y 15-19", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(15, 0, 0), secondBlockEndTime: new TimeSpan(19, 0, 0), secondBlockToleranceMinutes: 10);
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Partido", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Domingo }, overtimeAuthorized: true));
        return h;
    }

    /// <summary>"HH:mm" del lunes.</summary>
    private static AttendanceRecord[] Punches(AttendanceRecalculationHarness h, params string[] times) =>
        times.Select(time =>
        {
            var record = AttendanceRegressionFixtures.CreatePunch(Emp, Monday.Add(TimeSpan.Parse(time)), CheckType.CheckIn);
            h.Store.Records.Add(record);
            return record;
        }).ToArray();

    private static async Task<DailyAttendance> CalculateAsync(AttendanceRecalculationHarness h)
    {
        await h.RecalculateAsync(Monday, Monday);
        return h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);
    }

    [Theory]
    [InlineData(new[] { "09:00", "13:30", "15:00", "19:00" }, 30)]   // +30 en el bloque 1
    [InlineData(new[] { "10:00", "13:00", "15:00", "20:00" }, 0)]    // 1 h de retardo compensa 1 h de más
    [InlineData(new[] { "09:12", "13:00", "15:00", "19:20" }, 0)]    // cuenta desde 09:30: −30 +20
    [InlineData(new[] { "09:12", "13:00", "15:00", "19:45" }, 15)]   // −30 +45
    [InlineData(new[] { "09:00", "12:30", "15:00", "20:00" }, 30)]   // salida anticipada −30 +60
    [InlineData(new[] { "09:00", "13:00" }, 0)]                       // sin bloque 2: −240
    [InlineData(new[] { "06:00", "13:00", "15:00", "19:00" }, 0)]    // llegar antes no cuenta
    public async Task Overtime_IsTheNetSurplusOfBothBlocks(string[] times, int expectedOvertime)
    {
        var h = Harness();
        Punches(h, times);

        var day = await CalculateAsync(h);

        day.OvertimeMinutes.Should().Be(expectedOvertime);
    }

    [Fact]
    public async Task LateArrival_CountsWorkedTimeInThirtyMinuteBlocks_LikeAFixedShift()
    {
        var h = Harness();
        Punches(h, "09:41", "13:00", "15:00", "19:00");

        var day = await CalculateAsync(h);

        day.LateMinutes.Should().Be(41, "el retardo se reporta en minutos reales");
        day.TotalWorkedMinutes.Should().Be(420, "el bloque 1 cuenta desde las 10:00");
    }

    [Fact]
    public async Task EarlyReturn_BeforeTheMiddleOfTheBreak_IsTheSecondBlockEntry()
    {
        var h = Harness();
        var p = Punches(h, "09:00", "13:00", "13:50", "19:00");

        var day = await CalculateAsync(h);

        day.Block1CheckOutRecordId.Should().Be(p[1].Id);
        day.Block2CheckInRecordId.Should().Be(p[2].Id);
        day.TotalWorkedMinutes.Should().Be(480, "regresar antes no cuenta: el bloque 2 empieza a las 15:00");
        day.OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task LateBreak_AfterTheMiddleOfTheBreak_IsTheFirstBlockExit()
    {
        var h = Harness();
        var p = Punches(h, "09:00", "14:10", "15:00", "19:00");

        var day = await CalculateAsync(h);

        day.Block1CheckOutRecordId.Should().Be(p[1].Id);
        day.Block2CheckInRecordId.Should().Be(p[2].Id);
        day.OvertimeMinutes.Should().Be(70);
    }

    [Theory]
    [InlineData(new[] { "09:00", "13:05", "19:00" }, 1)]  // olvidó la entrada del bloque 2
    [InlineData(new[] { "09:00", "14:50", "19:00" }, 2)]  // olvidó la salida del bloque 1
    [InlineData(new[] { "09:00", "14:00", "19:00" }, 1)]  // empate: salida del bloque 1
    public async Task ThreePunches_TheMiddleOneGoesToTheNearestBreakBoundary(string[] times, int slot)
    {
        var h = Harness();
        var p = Punches(h, times);

        var day = await CalculateAsync(h);

        day.CheckInRecordId.Should().Be(p[0].Id);
        day.CheckOutRecordId.Should().Be(p[2].Id);
        if (slot == 1)
        {
            day.Block1CheckOutRecordId.Should().Be(p[1].Id);
            day.MissingBlock2CheckIn.Should().BeTrue();
        }
        else
        {
            day.Block2CheckInRecordId.Should().Be(p[1].Id);
            day.MissingBlock1CheckOut.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ForgottenFirstEntry_IsMissing_InsteadOfAFourHourTardiness()
    {
        var h = Harness();
        var p = Punches(h, "13:00", "15:00", "19:00");

        var day = await CalculateAsync(h);

        day.MissingCheckIn.Should().BeTrue();
        day.Block1CheckOutRecordId.Should().Be(p[0].Id);
        day.LateMinutes.Should().Be(0);
    }

    [Fact]
    public async Task ExitWithinABlock_IsATemporaryExit()
    {
        var h = Harness();
        Punches(h, "09:00", "11:00", "11:30", "13:00", "15:00", "19:00");

        var day = await CalculateAsync(h);

        day.HasTemporaryExits.Should().BeTrue();
        day.TemporaryExitMinutes.Should().Be(30);
        day.TotalWorkedMinutes.Should().Be(450, "se descuenta hasta que RH la clasifique");
        day.LunchMinutesTaken.Should().Be(0, "el partido no tiene comida");
    }

    [Fact]
    public async Task UnpairedPunchWithinABlock_IsAReviewIssue()
    {
        var h = Harness();
        Punches(h, "09:00", "11:00", "13:00", "15:00", "19:00");

        var day = await CalculateAsync(h);

        day.CalculationIssue.Should().Contain("sin pareja a las 11:00");
        day.TotalWorkedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task DoubleTap_IsOnePunch()
    {
        var h = Harness();
        var p = Punches(h, "09:00", "09:03", "13:00", "15:00", "19:00", "19:04");

        var day = await CalculateAsync(h);

        day.CheckInRecordId.Should().Be(p[0].Id);
        day.CheckOutRecordId.Should().Be(p[5].Id, "en una salida se conserva el último toque");
        day.TotalWorkedMinutes.Should().Be(484);
        day.CalculationIssue.Should().BeNullOrEmpty();
    }
}
