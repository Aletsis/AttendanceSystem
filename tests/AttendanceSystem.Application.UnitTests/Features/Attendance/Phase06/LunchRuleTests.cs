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
/// Regla de comida (F06-05) con el orquestador real: ventana global 12:30–15:00 y 30 min de referencia.
/// Con el descuento desactivado (por defecto) la comida se identifica sin descontarse; activado, se descuenta
/// completa y RH puede perdonarla.
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

    private static void EnableLunchDeduction(AttendanceRecalculationHarness h)
    {
        var config = SystemConfiguration.CreateDefault();
        config.SetLunchDeduction(true);
        h.Configuration = config;
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
    public async Task Lunch_WithDeductionDisabled_IsIdentified_AndNotDeducted()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 10), (13, 50), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(40);
        day.AttendanceNote.Should().Contain("Comida de 40 min — sin descuento");
        day.LunchExcessMinutes.Should().Be(0);
        day.LunchBreakMinutesApplied.Should().Be(0);
        day.HasTemporaryExits.Should().BeFalse("la comida no es una salida temporal");
        day.TotalWorkedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task Lunch_WithDeductionEnabled_DeductsTheWholeLunch()
    {
        var h = Harness();
        EnableLunchDeduction(h);
        Punches(h, (8, 0), (13, 0), (13, 40), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(40);
        day.LunchExcessMinutes.Should().Be(40);
        day.TotalWorkedMinutes.Should().Be(440);
        day.AttendanceNote.Should().Contain("Comida de 40 min — 40 min descontados");
    }

    [Theory]
    [InlineData(16, 420, 60, 0)]  // Sale a su hora programada: le falta reponer la hora de comida.
    [InlineData(17, 480, 0, 0)]   // Repone la comida: jornada completa.
    [InlineData(18, 540, 0, 60)]  // Una hora después de completar la jornada: 1 h extra.
    public async Task DeductedLunch_IsMadeUpAtTheEndOfTheJornada(int exitHour, int worked, int early, int overtime)
    {
        var h = Harness(); // 08:00–16:00
        EnableLunchDeduction(h);
        Punches(h, (8, 0), (13, 0), (14, 0), (exitHour, 0));

        var day = await CalculateAsync(h);

        day.LunchBreakMinutesApplied.Should().Be(60);
        day.TotalWorkedMinutes.Should().Be(worked);
        day.EarlyDepartureMinutes.Should().Be(early);
        day.RawOvertimeMinutes.Should().Be(overtime);
    }

    [Fact]
    public async Task WithoutLunch_TheJornadaEndsAtTheScheduledExit()
    {
        var h = Harness();
        EnableLunchDeduction(h);
        Punches(h, (8, 0), (17, 0));

        var day = await CalculateAsync(h);

        day.TotalWorkedMinutes.Should().Be(540);
        day.EarlyDepartureMinutes.Should().Be(0);
        day.RawOvertimeMinutes.Should().Be(60);
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
        day.TotalWorkedMinutes.Should().Be(450, "la salida temporal se descuenta hasta que RH la marque con goce");
    }

    [Theory]
    [InlineData(TemporaryExitStatus.ApprovedPaid, 480)]
    [InlineData(TemporaryExitStatus.Dismissed, 480)]
    [InlineData(TemporaryExitStatus.ApprovedUnpaid, 450)]
    public async Task TemporaryExit_ClassifiedByHR_IsDeductedOnlyWithoutPay(TemporaryExitStatus status, int worked)
    {
        var h = Harness();
        Punches(h, (8, 0), (10, 0), (10, 30), (16, 0));
        await CalculateAsync(h);

        var outcome = await new ClassifyTemporaryExitCommandHandler(h.Decisions())
            .Handle(new ClassifyTemporaryExitCommand(Emp.Value, DateOnly.FromDateTime(Monday), status, "rh"), CancellationToken.None);
        outcome.IsSuccess.Should().BeTrue(outcome.IsSuccess ? string.Empty : outcome.Error);

        var day = await CalculateAsync(h);
        day.TemporaryExitStatus.Should().Be(status);
        day.TotalWorkedMinutes.Should().Be(worked);
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
    public async Task TwoAbsencesInTheWindow_TheClosestToTheReferenceDurationIsLunch()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (13, 20), (14, 0), (14, 32), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(32, "32 min está más cerca de los 30 de referencia que 20 min");
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
    public async Task ShiftReferenceDuration_OverridesTheGlobalValue()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        shift.SetPaidLunchMinutes(60);
        var h = Harness(shift);
        Punches(h, (8, 0), (13, 0), (13, 30), (14, 0), (15, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(60, "el turno toma 60 min como referencia de la comida");
        day.TemporaryExitMinutes.Should().Be(30);
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
        EnableLunchDeduction(h);
        Punches(h, (8, 0), (13, 0), (13, 45), (17, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(45);
        day.LunchExcessMinutes.Should().Be(45);
        day.TotalWorkedMinutes.Should().Be(495);
    }

    [Fact]
    public async Task ForgivingTheLunch_IsAuditedAndSurvivesRecalculation()
    {
        var h = Harness();
        EnableLunchDeduction(h);
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

    [Fact]
    public async Task OrphanIntermediatePunch_IsAReviewIssue_NotATemporaryExit()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.HasTemporaryExits.Should().BeFalse("un marcaje sin pareja no es una salida temporal de 0 min");
        day.CalculationIssue.Should().Contain("sin pareja a las 13:00");
        day.RequiresReview.Should().BeTrue();
        day.LunchBreakMinutesApplied.Should().Be(0);
        day.TotalWorkedMinutes.Should().Be(480, "no se descuenta nada hasta que RH lo resuelva");
        day.AttendanceNote.Should().Contain("revisar");
    }

    [Fact]
    public async Task OrphanIntermediatePunch_ExcludedByHR_ClearsTheIssue()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (16, 0));
        await CalculateAsync(h);
        var orphan = h.Store.Records.Single(r => r.CheckTime == Monday.AddHours(13).AddHours(6));

        var result = await h.ManualAssign().Handle(new AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance.ManuallyAssignAttendanceCommand(
            Emp.Value, DateOnly.FromDateTime(Monday), orphan.Id.Value.ToString(),
            AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance.ManuallyAssignAttendanceCommandHandler.Unassign,
            Reason: "Marcaje de prueba"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);

        var day = await CalculateAsync(h);
        day.CalculationIssue.Should().BeNull();
        day.RequiresReview.Should().BeFalse();
        day.TotalWorkedMinutes.Should().Be(480);
        h.Store.Assignments.Single(x => x.Id == orphan.Id).Role.Should().Be(PunchAssignmentRole.ManuallyExcluded);

        // Restaurar devuelve el marcaje al cálculo automático y la incidencia reaparece.
        var restored = await h.ManualAssign().Handle(new AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance.ManuallyAssignAttendanceCommand(
            Emp.Value, DateOnly.FromDateTime(Monday), orphan.Id.Value.ToString(),
            AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance.ManuallyAssignAttendanceCommandHandler.Restore), CancellationToken.None);
        restored.IsSuccess.Should().BeTrue(restored.IsSuccess ? string.Empty : restored.Error);
        (await CalculateAsync(h)).CalculationIssue.Should().Contain("sin pareja a las 13:00");
    }
}
