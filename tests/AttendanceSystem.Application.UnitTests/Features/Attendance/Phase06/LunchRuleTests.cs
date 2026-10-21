using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase06;

/// <summary>
/// Regla de comida (F06-05) con el orquestador real: ventana global 12:30–15:00 y 30 min con goce.
/// Casos acordados: comida sin hora fija, comida de 30 min no descontada, salida temporal sin comida,
/// comida de 40 min (10 de exceso descontados), salida temporal y comida el mismo día.
/// </summary>
public class LunchRuleTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-LUN-001");

    private static AttendanceRecalculationHarness Harness(Shift? shift = null)
    {
        shift ??= AttendanceRegressionFixtures.CreateRegularShift(); // 08:00–16:00
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: Array.Empty<WeekDay>(), overtimeAuthorized: true));
        return h;
    }

    private static void Punches(AttendanceRecalculationHarness h, params (int H, int M)[] times)
    {
        foreach (var (hour, minute) in times)
        {
            h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddHours(hour).AddMinutes(minute), CheckType.CheckIn));
        }
    }

    private static async Task<DailyAttendance> CalculateAsync(AttendanceRecalculationHarness h)
    {
        await h.RecalculateAsync(Monday, Monday);
        return h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);
    }

    [Fact]
    public async Task Lunch_OfThirtyMinutes_IsPaid_AndNotDeducted()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 10), (13, 40), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(30);
        day.LunchExcessMinutes.Should().Be(0);
        day.LunchBreakMinutesApplied.Should().Be(0);
        day.HasTemporaryExits.Should().BeFalse("la comida no es una salida temporal");
        day.TotalWorkedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task Lunch_OfFortyMinutes_DeductsTheExcessByDefault()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (13, 40), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(40);
        day.LunchExcessMinutes.Should().Be(10);
        day.TotalWorkedMinutes.Should().Be(470);
        day.AttendanceNote.Should().Contain("10 min de exceso descontados");
    }

    [Fact]
    public async Task TemporaryExit_OutsideTheWindow_IsNotLunch()
    {
        var h = Harness();
        Punches(h, (8, 0), (10, 0), (10, 30), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(0);
        day.HasTemporaryExits.Should().BeTrue();
        day.TemporaryExitMinutes.Should().Be(30);
        day.TemporaryExitStatus.Should().Be(TemporaryExitStatus.Pending);
        day.TotalWorkedMinutes.Should().Be(480, "la salida temporal pendiente no se descuenta hasta que RH la clasifique");
    }

    [Fact]
    public async Task TemporaryExit_AndLunch_OnTheSameDay_AreSeparated()
    {
        var h = Harness();
        Punches(h, (8, 0), (10, 0), (10, 20), (13, 30), (14, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(30);
        day.HasTemporaryExits.Should().BeTrue();
        day.TemporaryExitMinutes.Should().Be(20);
    }

    [Fact]
    public async Task TwoAbsencesInTheWindow_TheClosestToThePaidDurationIsLunch()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (13, 20), (14, 0), (14, 32), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(32, "32 min está más cerca de los 30 con goce que 20 min");
        day.LunchExcessMinutes.Should().Be(2);
        day.TemporaryExitMinutes.Should().Be(20);
    }

    [Fact]
    public async Task NoLunchPunches_NothingIsDeducted()
    {
        var h = Harness();
        Punches(h, (8, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(0);
        day.LunchBreakMinutesApplied.Should().Be(0);
        day.TotalWorkedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task ShiftPaidMinutes_OverrideTheGlobalValue()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        shift.SetPaidLunchMinutes(60);
        var h = Harness(shift);
        Punches(h, (8, 0), (13, 0), (14, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(60);
        day.LunchExcessMinutes.Should().Be(0, "el turno concede 60 min con goce");
    }

    [Fact]
    public async Task GlobalWindow_IsTakenFromTheSystemConfiguration()
    {
        var h = Harness();
        var config = SystemConfiguration.CreateDefault();
        config.UpdateLunchSettings(new TimeSpan(14, 0, 0), new TimeSpan(16, 0, 0), 30);
        h.Configuration = config;
        Punches(h, (8, 0), (13, 0), (13, 30), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(0, "las 13:00 quedan fuera de la ventana configurada");
        day.HasTemporaryExits.Should().BeTrue();
    }

    [Fact]
    public async Task FlexibleShift_UsesTheSameRule()
    {
        var shift = Shift.Create("Flexible 8h", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Flexible);
        var h = Harness(shift);
        Punches(h, (8, 0), (13, 0), (13, 45), (17, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(45);
        day.LunchExcessMinutes.Should().Be(15);
        day.TotalWorkedMinutes.Should().Be(525);
    }

    [Fact]
    public async Task ForgivingTheExcess_IsAuditedAndSurvivesRecalculation()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (13, 40), (16, 0));
        await CalculateAsync(h);

        var handler = new SetLunchExcessForgivenCommandHandler(h.Decisions());
        var outcome = await handler.Handle(new SetLunchExcessForgivenCommand(Emp.Value, DateOnly.FromDateTime(Monday), true, "Autorizado por supervisor"), CancellationToken.None);
        outcome.IsSuccess.Should().BeTrue(outcome.IsSuccess ? string.Empty : outcome.Error);

        var day = await CalculateAsync(h);
        day.LunchExcessForgiven.Should().BeTrue();
        day.LunchBreakMinutesApplied.Should().Be(0);
        day.TotalWorkedMinutes.Should().Be(480);
        day.AttendanceNote.Should().Contain("perdonado");
        h.Store.AdjustmentHistory.Should().ContainSingle(e => e.EmployeeId == Emp);

        (await handler.Handle(new SetLunchExcessForgivenCommand(Emp.Value, DateOnly.FromDateTime(Monday), true), CancellationToken.None))
            .IsFailure.Should().BeTrue("una decisión que no cambia nada se rechaza");
    }
}
