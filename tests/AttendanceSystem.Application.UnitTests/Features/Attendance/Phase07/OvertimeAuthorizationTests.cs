using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 12: el excedente de un empleado sin extras autorizadas no se paga, queda en una nota y RH puede autorizar
/// el día; el horario flexible no admite extras antes de la entrada.
/// </summary>
public class OvertimeAuthorizationTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-OT-012");

    private static Employee NewEmployee(Shift shift, bool authorized, bool beforeEntry = false) =>
        Employee.Create(Emp, "Empleado", "Extras", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Domingo }, overtimeAuthorized: authorized,
            calculateOvertimeBeforeEntry: beforeEntry);

    private static async Task<(AttendanceRecalculationHarness H, DailyAttendance Day)> CalculateAsync(bool authorized, params double[] hours)
    {
        var shift = Shift.Create("Fijo 9-17", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = new AttendanceRecalculationHarness { Configuration = SystemConfiguration.CreateDefault() };
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(NewEmployee(shift, authorized));
        foreach (var hour in hours)
            h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddMinutes(Math.Round(hour * 60)), CheckType.CheckIn));
        await h.RecalculateAsync(Monday, Monday.AddDays(1));
        return (h, Day(h));
    }

    private static DailyAttendance Day(AttendanceRecalculationHarness h) => h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);

    private static async Task DecideAsync(AttendanceRecalculationHarness h, bool? authorized, int? minutes = null)
    {
        var outcome = await AuthorizeAsync(h, authorized, minutes, minutes.HasValue ? Day(h).RawOvertimeMinutes : null);
        outcome.IsSuccess.Should().BeTrue(outcome.IsSuccess ? string.Empty : outcome.Error);
    }

    private static Task<AttendanceSystem.Application.Common.Result<AttendanceDecisionOutcome>> AuthorizeAsync(
        AttendanceRecalculationHarness h, bool? authorized, int? minutes, int? forRaw) =>
        new SetDailyOvertimeAuthorizationCommandHandler(h.Decisions()).Handle(
            new SetDailyOvertimeAuthorizationCommand(Emp.Value, DateOnly.FromDateTime(Monday), authorized, authorized.HasValue ? false : null,
                AuthorizedMinutes: minutes, ForRawMinutes: forRaw),
            CancellationToken.None);

    [Theory]
    [InlineData(80, 80)]   // completo, sin redondeo
    [InlineData(60, 60)]   // 1 h de 80 min
    [InlineData(30, 30)]
    public async Task HrAuthorizesTheFullSurplusOrA30MinuteBlock(int authorized, int expected)
    {
        var (h, _) = await CalculateAsync(false, 9, 18 + 20 / 60.0);

        await DecideAsync(h, true, authorized);

        Day(h).OvertimeMinutes.Should().Be(expected);
        Day(h).HasUnauthorizedOvertime.Should().BeFalse();
        if (authorized < 80)
            Day(h).Notes.Should().Contain($"Tiempo extra autorizado: {authorized} de 80 min");
        else
            Day(h).Notes.Should().NotContain(n => n.StartsWith("Tiempo extra"));
    }

    [Theory]
    [InlineData(45, 80)]   // ni completo ni bloque de 30
    [InlineData(0, 80)]
    [InlineData(90, 80)]   // más que el excedente
    public async Task Authorization_MustBeTheFullSurplusOrA30MinuteBlock(int minutes, int forRaw)
    {
        var (h, _) = await CalculateAsync(false, 9, 18 + 20 / 60.0);

        var outcome = await AuthorizeAsync(h, true, minutes, forRaw);

        outcome.IsFailure.Should().BeTrue();
        Day(h).OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task RecalculatingWithTheSameSurplus_KeepsTheAuthorization()
    {
        var (h, _) = await CalculateAsync(false, 9, 18 + 20 / 60.0);
        await DecideAsync(h, true, 60);

        await h.RecalculateAsync(Monday, Monday.AddDays(1));

        Day(h).OvertimeMinutes.Should().Be(60);
        Day(h).IsOvertimeAuthorizationOutdated.Should().BeFalse();
    }

    [Fact]
    public async Task RecalculatingWithADifferentSurplus_AsksForAuthorizationAgain()
    {
        var (h, _) = await CalculateAsync(false, 9, 18 + 20 / 60.0);
        await DecideAsync(h, true, 60);

        // Aparece una salida posterior: el excedente pasa de 80 a 110 min.
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddHours(18).AddMinutes(50), CheckType.CheckIn));
        await h.RecalculateAsync(Monday, Monday.AddDays(1));

        Day(h).RawOvertimeMinutes.Should().Be(110);
        Day(h).OvertimeMinutes.Should().Be(0);
        Day(h).IsOvertimeAuthorizationOutdated.Should().BeTrue();
        Day(h).HasUnauthorizedOvertime.Should().BeTrue();
        Day(h).Notes.Should().Contain("El tiempo extra cambió de 80 a 110 min: autorícelo nuevamente");

        await DecideAsync(h, true, 110);
        Day(h).OvertimeMinutes.Should().Be(110);
        Day(h).IsOvertimeAuthorizationOutdated.Should().BeFalse();
    }

    [Fact]
    public async Task WithdrawingClearsTheAuthorization()
    {
        var (h, _) = await CalculateAsync(false, 9, 18 + 20 / 60.0);
        await DecideAsync(h, true, 60);

        await DecideAsync(h, null);

        Day(h).OvertimeMinutes.Should().Be(0);
        Day(h).AuthorizedOvertimeMinutes.Should().BeNull();
        Day(h).Notes.Should().Contain("Tiempo extra no autorizado: 80 min");
    }

    [Fact]
    public void UnauthorizedEmployees_KeepNoRoundingCapsOrBeforeEntry()
    {
        var shift = Shift.Create("Fijo", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var employee = Employee.Create(Emp, "Empleado", "Extras", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Matutino, shift.Id, restDays: new[] { WeekDay.Domingo }, overtimeAuthorized: false,
            overtimeCalculationMethod: OvertimeCalculationMethod.RoundByHour, overtimeCapType: OvertimeCapType.Daily, overtimeCapMinutes: 60,
            calculateOvertimeBeforeEntry: true);

        employee.OvertimeCalculationMethod.Should().Be(OvertimeCalculationMethod.NoRounding);
        employee.OvertimeCapType.Should().Be(OvertimeCapType.None);
        employee.OvertimeCapMinutes.Should().BeNull();
        employee.CalculateOvertimeBeforeEntry.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthorized_SurplusIsNotPaid_AndLeavesANote()
    {
        var (_, day) = await CalculateAsync(false, 9, 18 + 20 / 60.0);

        day.RawOvertimeMinutes.Should().Be(80);
        day.OvertimeMinutes.Should().Be(0);
        day.HasUnauthorizedOvertime.Should().BeTrue();
        day.Notes.Should().Contain("Tiempo extra no autorizado: 80 min");
    }

    [Fact]
    public async Task Authorized_HasNoUnauthorizedNote()
    {
        var (_, day) = await CalculateAsync(true, 9, 18 + 20 / 60.0);

        day.OvertimeMinutes.Should().Be(80);
        day.HasUnauthorizedOvertime.Should().BeFalse();
        day.Notes.Should().NotContain(n => n.StartsWith("Tiempo extra no autorizado"));
    }

    [Fact]
    public async Task NoSurplus_NoNote()
    {
        var (_, day) = await CalculateAsync(false, 9, 17);

        day.HasUnauthorizedOvertime.Should().BeFalse();
    }

    [Theory]
    [InlineData(ShiftType.Flexible, false)]
    [InlineData(ShiftType.Matutino, true)]
    [InlineData(ShiftType.Rotativo, true)]
    public void BeforeEntryOvertime_IsNotAllowedForFlexibleEmployees(ShiftType type, bool expected)
    {
        var shift = type == ShiftType.Flexible
            ? Shift.Create("Flex", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Flexible, flexWindowEndTime: new TimeSpan(10, 0, 0))
            : Shift.Create("Fijo", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var employee = Employee.Create(Emp, "Empleado", "Extras", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            type, shift.Id, restDays: new[] { WeekDay.Domingo }, overtimeAuthorized: true, calculateOvertimeBeforeEntry: true);

        employee.CalculateOvertimeBeforeEntry.Should().Be(expected);

        employee.Update("Empleado", "Extras", null, null, new DateTime(2024, 1, 1), Gender.Male, EmployeeStatus.Alta,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Flexible, shift.Id, overtimeAuthorized: true, calculateOvertimeBeforeEntry: true, restDays: new[] { WeekDay.Domingo });
        employee.CalculateOvertimeBeforeEntry.Should().BeFalse();
    }
}
