using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase06;

/// <summary>
/// Decisión 4b: marcajes intermedios del turno nocturno 22:00–06:00 con la ventana de comida nocturna global
/// (por defecto 23:00–04:00, cruza medianoche) y salida aceptada desde 2 h antes de la programada. Caso 4: la entrada
/// se busca hasta el inicio de la ventana de salida; un marcaje solo y la salida anticipada siguen la regla del turno fijo.
/// </summary>
public class NightShiftRuleTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-NOC-001");

    private static AttendanceRecalculationHarness Harness()
    {
        var shift = AttendanceRegressionFixtures.CreateNightShift();
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Nocturno", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Martes }, overtimeAuthorized: true));
        return h;
    }

    /// <summary>Horas relativas al lunes: 22 = lunes 22:00, 26.5 = martes 02:30.</summary>
    private static AttendanceRecord[] Punches(AttendanceRecalculationHarness h, params double[] hours) =>
        hours.Select(hour =>
        {
            var record = AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddHours(hour), CheckType.CheckIn);
            h.Store.Records.Add(record);
            return record;
        }).ToArray();

    private static async Task<DailyAttendance> CalculateAsync(AttendanceRecalculationHarness h)
    {
        await h.RecalculateAsync(Monday, Monday.AddDays(1));
        return h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);
    }

    [Fact]
    public async Task LunchAfterMidnight_IsPaid()
    {
        var h = Harness();
        var p = Punches(h, 22, 26, 26.5, 30);

        var day = await CalculateAsync(h);

        day.CheckOutRecordId.Should().Be(p[3].Id);
        day.LunchMinutesTaken.Should().Be(30);
        day.LunchExcessMinutes.Should().Be(0);
        day.HasTemporaryExits.Should().BeFalse();
    }

    [Fact]
    public async Task LunchCrossingMidnight_IsRecognized_AndDeducted()
    {
        var h = Harness();
        var config = SystemConfiguration.CreateDefault();
        config.UpdateLunchSettings(new TimeSpan(12, 30, 0), new TimeSpan(15, 0, 0), 0); // sin minutos con goce
        h.Configuration = config;
        Punches(h, 22, 23 + 50 / 60.0, 24.5, 30); // 23:50 → 00:30 = 40 min

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(40);
        day.LunchExcessMinutes.Should().Be(40);
        day.LunchBreakMinutesApplied.Should().Be(40);
    }

    [Fact]
    public async Task TemporaryExitOutsideTheNightWindow_IsATemporaryExit()
    {
        var h = Harness();
        Punches(h, 22, 25, 25.5, 28.5, 29, 30); // comida 01:00–01:30, salida 04:30–05:00

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(30);
        day.HasTemporaryExits.Should().BeTrue();
        day.TemporaryExitMinutes.Should().Be(30, "04:30–05:00 queda fuera de la ventana 23:00–04:00");
    }

    [Fact]
    public async Task MidnightPunch_WithoutPair_IsAReviewIssue()
    {
        var h = Harness();
        Punches(h, 22, 24, 30);

        var day = await CalculateAsync(h);

        day.CalculationIssue.Should().Contain("sin pareja a las 00:00");
        day.RequiresReview.Should().BeTrue();
        day.TotalWorkedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task ExitBeforeTheWindow_AfterALunch_IsAnEarlyDeparture()
    {
        var h = Harness();
        var p = Punches(h, 22, 26, 26.5);

        var day = await CalculateAsync(h);

        day.CheckOutRecordId.Should().Be(p[2].Id, "como en turno fijo, el último marcaje después de la entrada es la salida");
        day.EarlyDepartureMinutes.Should().Be(210);
        day.CalculationIssue.Should().Contain("sin pareja a las 02:00");
    }

    [Fact]
    public async Task ExitBeforeTheWindow_IsAnEarlyDeparture()
    {
        var h = Harness();
        var p = Punches(h, 22, 27); // sale a las 03:00

        var day = await CalculateAsync(h);

        day.CheckInRecordId.Should().Be(p[0].Id);
        day.CheckOutRecordId.Should().Be(p[1].Id);
        day.MissingCheckOut.Should().BeFalse();
        day.EarlyDepartureMinutes.Should().Be(180);
        day.TotalWorkedMinutes.Should().Be(300);
    }

    [Fact]
    public async Task LateArrival_UpToTheExitWindow_IsTheEntry()
    {
        var h = Harness();
        var p = Punches(h, 26.5, 30); // entra 02:30, sale 06:00

        var day = await CalculateAsync(h);

        day.CheckInRecordId.Should().Be(p[0].Id, "la ventana de entrada llega hasta el inicio de la de salida (04:00)");
        day.CheckOutRecordId.Should().Be(p[1].Id);
        day.LateMinutes.Should().BeGreaterThan(0);
        day.MissingCheckIn.Should().BeFalse();
    }

    [Theory]
    [InlineData(27, false)]   // 03:00: más cerca de las 06:00 → salida
    [InlineData(25.5, true)]  // 01:30: más cerca de las 22:00 → entrada
    [InlineData(26, false)]   // 02:00: empate → salida
    public async Task LonePunch_GoesToTheNearestScheduledBoundary(double hour, bool isEntry)
    {
        var h = Harness();
        var p = Punches(h, hour);

        var day = await CalculateAsync(h);

        day.IsAbsent.Should().BeFalse();
        if (isEntry)
        {
            day.CheckInRecordId.Should().Be(p[0].Id);
            day.MissingCheckOut.Should().BeTrue();
        }
        else
        {
            day.CheckOutRecordId.Should().Be(p[0].Id);
            day.MissingCheckIn.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DoubleTapNearTheExit_CountsAsTheExit()
    {
        var h = Harness();
        var p = Punches(h, 27, 27.1); // 03:00 y 03:06

        var day = await CalculateAsync(h);

        day.CheckInRecordId.Should().BeNull();
        day.CheckOutRecordId.Should().Be(p[1].Id);
    }

    [Fact]
    public async Task LateExtraPunch_NextMorning_IsTheExit_AndTheRealExitIsAReviewNote()
    {
        var h = Harness();
        var p = Punches(h, 22, 30, 36); // sale 06:00 y vuelve a marcar a las 12:00

        var day = await CalculateAsync(h);

        day.CheckOutRecordId.Should().Be(p[2].Id);
        day.CalculationIssue.Should().Contain("sin pareja a las 06:00");
        day.Notes.Should().Contain(n => n.Contains("sin pareja a las 06:00"));
    }

    [Fact]
    public async Task NightWindow_IsTakenFromTheSystemConfiguration()
    {
        var h = Harness();
        var config = SystemConfiguration.CreateDefault();
        config.UpdateNightLunchWindow(new TimeSpan(2, 0, 0), new TimeSpan(3, 0, 0));
        h.Configuration = config;
        Punches(h, 22, 25, 25.5, 30);

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(0, "01:00 queda fuera de la ventana configurada 02:00–03:00");
        day.HasTemporaryExits.Should().BeTrue();
    }
}
