using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 3 de la auditoría: precedencia ajuste → horario fijo → roster (sólo rotativos) → autodetección entre
/// matutino, vespertino y nocturno; falta sin turno ni marcajes; descansos con vigencia.
/// </summary>
public class ShiftResolutionRulesTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-RES-001");

    private static Employee CreateEmployee(ShiftType? type, Shift? schedule, params WeekDay[] restDays) =>
        Employee.Create(Emp, "Empleado", "Resolucion", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            type, schedule?.Id, restDays: restDays);

    private static void Punch(AttendanceRecalculationHarness h, DateTime at) =>
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, at, CheckType.CheckIn));

    [Fact]
    public async Task WithoutSchedule_AutoDetectsOnlyMorningAfternoonOrNightShifts()
    {
        var h = new AttendanceRecalculationHarness();
        var partido = Shift.Create("Partido 08", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(14, 0, 0), secondBlockEndTime: new TimeSpan(18, 0, 0));
        var morning = Shift.Create("Matutino 09", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        h.Store.Shifts.AddRange(new[] { partido, morning });
        h.Store.Employees.Add(CreateEmployee(null, null));
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(17));

        await h.RecalculateAsync(Monday, Monday);

        var day = h.Daily(Emp, Monday)!;
        day.ShiftId.Should().Be(morning.Id, "el partido de las 08:00 está más cerca pero no es candidato");
        day.IsAutoDetectedShift.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutShift_AndWithoutPunches_OnAWorkday_IsAnAbsence()
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(AttendanceRegressionFixtures.CreateRegularShift());
        h.Store.Employees.Add(CreateEmployee(null, null, WeekDay.Domingo));

        await h.RecalculateAsync(Monday, Monday.AddDays(6));

        h.Daily(Emp, Monday)!.IsAbsent.Should().BeTrue();
        var sunday = h.Daily(Emp, Monday.AddDays(6))!;
        sunday.IsRestDay.Should().BeTrue();
        sunday.IsAbsent.Should().BeFalse();
    }

    [Fact]
    public async Task FixedSchedule_IgnoresTheRoster()
    {
        var h = new AttendanceRecalculationHarness();
        var morning = AttendanceRegressionFixtures.CreateRegularShift();
        var afternoon = Shift.Create("Vespertino 14", new TimeSpan(14, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);
        h.Store.Shifts.AddRange(new[] { morning, afternoon });
        h.Store.Employees.Add(CreateEmployee(ShiftType.Matutino, morning));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, afternoon.Id, isRestDay: false));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday.AddDays(1), null, isRestDay: true));

        await h.RecalculateAsync(Monday, Monday.AddDays(1));

        h.Daily(Emp, Monday)!.ShiftId.Should().Be(morning.Id);
        h.Daily(Emp, Monday.AddDays(1))!.IsRestDay.Should().BeFalse("el descanso del roster no aplica a un horario fijo");
    }

    [Fact]
    public async Task RotatingEmployee_FollowsTheRoster_AndAutoDetectsDaysWithoutIt()
    {
        var h = new AttendanceRecalculationHarness();
        var morning = AttendanceRegressionFixtures.CreateRegularShift(); // 08:00
        var afternoon = Shift.Create("Vespertino 14", new TimeSpan(14, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);
        h.Store.Shifts.AddRange(new[] { morning, afternoon });
        h.Store.Employees.Add(CreateEmployee(ShiftType.Rotativo, null));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, afternoon.Id, isRestDay: false));
        Punch(h, Monday.AddHours(14));
        Punch(h, Monday.AddHours(22));
        Punch(h, Monday.AddDays(1).AddHours(14).AddMinutes(5));
        Punch(h, Monday.AddDays(1).AddHours(22));

        await h.RecalculateAsync(Monday, Monday.AddDays(1));

        h.Daily(Emp, Monday)!.ShiftId.Should().Be(afternoon.Id);
        h.Daily(Emp, Monday)!.IsAutoDetectedShift.Should().BeFalse();
        var tuesday = h.Daily(Emp, Monday.AddDays(1))!;
        tuesday.ShiftId.Should().Be(afternoon.Id);
        tuesday.IsAutoDetectedShift.Should().BeTrue("sin roster ese día se detecta por el primer marcaje");
    }

    [Fact]
    public void RestDays_AreDated_LikeTheSchedule()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var employee = CreateEmployee(ShiftType.Matutino, shift, WeekDay.Domingo);
        var effective = Monday.AddDays(7);

        var recalculateFrom = employee.Update("Empleado", "Resolucion", null, null, new DateTime(2024, 1, 1), Gender.Male,
            EmployeeStatus.Alta, AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId,
            AttendanceRegressionFixtures.PosOperatorId, ShiftType.Matutino, shift.Id,
            restDays: new[] { WeekDay.Lunes }, effectiveFrom: effective);

        recalculateFrom.Should().Be(effective, "cambiar los descansos recalcula desde su vigencia");
        employee.IsRestDayOn(Monday.AddDays(6)).Should().BeTrue("antes de la vigencia descansa el domingo");
        employee.IsRestDayOn(Monday).Should().BeFalse();
        employee.IsRestDayOn(effective).Should().BeTrue("desde la vigencia descansa el lunes");
        employee.IsRestDayOn(effective.AddDays(6)).Should().BeFalse();
    }
}
