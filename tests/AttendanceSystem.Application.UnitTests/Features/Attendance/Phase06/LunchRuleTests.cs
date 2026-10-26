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
/// Regla de comida (caso 11) con el orquestador real, turno 08:00–16:00, ventana global 12:30–15:00 y 30 min de
/// comida con goce. Toda ausencia es una salida que RH clasifica por separado; con la ventana activa, la primera
/// ausencia que la toca es comida y sólo descuenta su excedente. Lo descontado se repone al final de la jornada.
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

    private static void Configure(AttendanceRecalculationHarness h, bool windowEnabled = true, int paidMinutes = 30)
    {
        var config = SystemConfiguration.CreateDefault();
        config.UpdateLunchSettings(new TimeSpan(12, 30, 0), new TimeSpan(15, 0, 0), paidMinutes);
        config.SetLunchWindowEnabled(windowEnabled);
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

    private static async Task ClassifyAsync(AttendanceRecalculationHarness h, TemporaryExit exit, TemporaryExitStatus? status)
    {
        var outcome = await new ClassifyTemporaryExitCommandHandler(h.Decisions())
            .Handle(new ClassifyTemporaryExitCommand(Emp.Value, DateOnly.FromDateTime(Monday), exit.ExitRecordId, status, "rh"), CancellationToken.None);
        outcome.IsSuccess.Should().BeTrue(outcome.IsSuccess ? string.Empty : outcome.Error);
    }

    [Theory]
    [InlineData(20, 0, 480)]   // dentro de los 30 con goce
    [InlineData(30, 0, 480)]
    [InlineData(50, 20, 460)]  // sólo se descuenta el excedente
    [InlineData(150, 120, 360)]
    public async Task WindowOn_Lunch_DeductsOnlyTheExcess(int lunch, int excess, int worked)
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (13 + lunch / 60, lunch % 60), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(lunch);
        day.LunchExcessMinutes.Should().Be(excess);
        day.TotalWorkedMinutes.Should().Be(worked);
        day.HasTemporaryExits.Should().BeFalse("la comida no es una salida temporal");
        day.RequiresReview.Should().BeFalse("la comida ya está clasificada");
        day.TemporaryExits.Should().ContainSingle(e => e.Status == TemporaryExitStatus.Lunch && e.IsAutomaticLunch);
    }

    [Fact]
    public async Task WindowOn_AnAbsenceThatTouchesTheWindow_IsLunch()
    {
        var h = Harness();
        Punches(h, (8, 0), (12, 0), (12, 40), (16, 0)); // sale antes de la ventana y regresa dentro

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(40);
        day.HasTemporaryExits.Should().BeFalse();
    }

    [Fact]
    public async Task WindowOn_TheFirstAbsenceInTheWindowIsLunch_TheNextIsATemporaryExit()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (13, 20), (13, 40), (14, 20), (16, 40));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(20);
        day.TemporaryExitMinutes.Should().Be(40);
        day.TemporaryExitStatus.Should().Be(TemporaryExitStatus.Pending);
        day.TotalWorkedMinutes.Should().Be(480, "520 menos los 40 de la salida pendiente");
        day.EarlyDepartureMinutes.Should().Be(0, "repuso la salida saliendo 16:40");
    }

    [Fact]
    public async Task WindowOff_EveryAbsenceIsAPendingTemporaryExit()
    {
        var h = Harness();
        Configure(h, windowEnabled: false);
        Punches(h, (8, 0), (13, 0), (14, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.LunchMinutesTaken.Should().Be(0);
        day.HasTemporaryExits.Should().BeTrue();
        day.TemporaryExitMinutes.Should().Be(60);
        day.TotalWorkedMinutes.Should().Be(420);
        day.EarlyDepartureMinutes.Should().Be(60, "lo descontado no se repuso");
        day.RequiresReview.Should().BeTrue();
        day.Notes.Should().Contain("Salida temporal 13:00–14:00 (60 min) — descontada, pendiente de clasificar");
    }

    [Theory]
    [InlineData(TemporaryExitStatus.Lunch, 450, 30)]          // comida: 30 con goce, 30 de excedente
    [InlineData(TemporaryExitStatus.ApprovedPaid, 480, 0)]
    [InlineData(TemporaryExitStatus.Dismissed, 480, 0)]
    [InlineData(TemporaryExitStatus.ApprovedUnpaid, 420, 60)]
    public async Task EachExit_IsClassifiedByHR(TemporaryExitStatus status, int worked, int early)
    {
        var h = Harness();
        Configure(h, windowEnabled: false);
        Punches(h, (8, 0), (13, 0), (14, 0), (16, 0));
        var exit = (await CalculateAsync(h)).TemporaryExits.Single();

        await ClassifyAsync(h, exit, status);

        var day = await CalculateAsync(h);
        day.TemporaryExits.Single().Status.Should().Be(status);
        day.TotalWorkedMinutes.Should().Be(worked);
        day.EarlyDepartureMinutes.Should().Be(early);
        day.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public async Task TwoExits_AreClassifiedSeparately()
    {
        var h = Harness();
        Punches(h, (8, 0), (10, 0), (10, 30), (11, 0), (11, 20), (16, 0));
        var exits = (await CalculateAsync(h)).TemporaryExits;
        exits.Should().HaveCount(2);

        await ClassifyAsync(h, exits[0], TemporaryExitStatus.ApprovedPaid);

        var day = await CalculateAsync(h);
        day.TemporaryExits.Select(e => e.Status).Should().Equal(TemporaryExitStatus.ApprovedPaid, TemporaryExitStatus.Pending);
        day.TotalWorkedMinutes.Should().Be(460, "sólo se descuenta la salida de 20 min que sigue pendiente");
        day.TemporaryExitStatus.Should().Be(TemporaryExitStatus.Pending);
        day.RequiresReview.Should().BeTrue();

        await ClassifyAsync(h, exits[1], TemporaryExitStatus.ApprovedUnpaid);
        day = await CalculateAsync(h);
        day.TemporaryExitStatus.Should().Be(TemporaryExitStatus.ApprovedUnpaid);
        day.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public async Task HR_CanReclassifyTheAutomaticLunch_AndRemoveTheDecision()
    {
        var h = Harness();
        Punches(h, (8, 0), (13, 0), (15, 0), (16, 0)); // "comida" de 2 h que en realidad fue cita médica
        var lunch = (await CalculateAsync(h)).TemporaryExits.Single();
        lunch.Status.Should().Be(TemporaryExitStatus.Lunch);

        await ClassifyAsync(h, lunch, TemporaryExitStatus.ApprovedPaid);
        (await CalculateAsync(h)).TotalWorkedMinutes.Should().Be(480);

        await ClassifyAsync(h, lunch, null);
        var day = await CalculateAsync(h);
        day.TemporaryExits.Single().Status.Should().Be(TemporaryExitStatus.Lunch, "sin decisión vuelve a la comida automática");
        day.TotalWorkedMinutes.Should().Be(390);
    }

    [Fact]
    public async Task Pending_IsNotAClassification()
    {
        var h = Harness();
        Punches(h, (8, 0), (10, 0), (10, 30), (16, 0));
        var exit = (await CalculateAsync(h)).TemporaryExits.Single();

        var outcome = await new ClassifyTemporaryExitCommandHandler(h.Decisions())
            .Handle(new ClassifyTemporaryExitCommand(Emp.Value, DateOnly.FromDateTime(Monday), exit.ExitRecordId, TemporaryExitStatus.Pending), CancellationToken.None);

        outcome.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(16, 450, 30)]  // sale a su hora: le falta reponer la salida
    [InlineData(17, 510, 0)]   // la repone saliendo después
    public async Task PendingExit_IsMadeUpAtTheEndOfTheJornada(int exitHour, int worked, int early)
    {
        var h = Harness();
        Punches(h, (8, 0), (10, 0), (10, 30), (exitHour, 0));

        var day = await CalculateAsync(h);

        day.TotalWorkedMinutes.Should().Be(worked);
        day.EarlyDepartureMinutes.Should().Be(early);
    }

    [Fact]
    public async Task ZeroPaidMinutes_DeductsTheWholeLunch_WhichIsMadeUpAtTheEnd()
    {
        var h = Harness();
        Configure(h, paidMinutes: 0);
        Punches(h, (8, 0), (13, 0), (14, 0), (17, 0));

        var day = await CalculateAsync(h);

        day.LunchExcessMinutes.Should().Be(60);
        day.TotalWorkedMinutes.Should().Be(480);
        day.EarlyDepartureMinutes.Should().Be(0);
        day.RawOvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task NoIntermediatePunches_NothingIsDeducted()
    {
        var h = Harness();
        Configure(h, windowEnabled: false);
        Punches(h, (8, 0), (16, 0));

        var day = await CalculateAsync(h);

        day.TemporaryExits.Should().BeEmpty();
        day.TotalWorkedMinutes.Should().Be(480, "sin marcajes el turno ya contempla la comida");
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
        day.LunchExcessMinutes.Should().Be(0, "el turno da 60 min de comida con goce");
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
        day.AttendanceNote.Should().Contain("clasifíquelo como error de checada o agregue la checada faltante");
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
