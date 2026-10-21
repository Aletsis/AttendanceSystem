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
/// (por defecto 23:00–04:00, cruza medianoche) y salida aceptada desde 2 h antes de la programada.
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
    public async Task LunchCrossingMidnight_IsRecognized_AndItsExcessDeducted()
    {
        var h = Harness();
        Punches(h, 22, 23 + 50 / 60.0, 24.5, 30); // 23:50 → 00:30 = 40 min

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(40);
        day.LunchExcessMinutes.Should().Be(10);
        day.LunchBreakMinutesApplied.Should().Be(10);
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
    public async Task MissingExit_TheLunchReturnIsNotTakenAsExit()
    {
        var h = Harness();
        Punches(h, 22, 26, 26.5);

        var day = await CalculateAsync(h);

        day.CheckOutRecordId.Should().BeNull("el regreso de comida de las 02:30 es anterior a la ventana de salida (04:00)");
        day.MissingCheckOut.Should().BeTrue();
        day.LunchMinutesTaken.Should().Be(30);
    }

    [Fact]
    public async Task EarlyExitBeforeTheWindow_IsReportedForReview()
    {
        var h = Harness();
        Punches(h, 22, 27); // sale a las 03:00

        var day = await CalculateAsync(h);

        day.CheckOutRecordId.Should().BeNull();
        day.CalculationIssue.Should().Contain("posible salida anticipada");
        day.RequiresReview.Should().BeTrue();
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
