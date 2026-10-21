using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase06;

/// <summary>
/// Decisión de cierre F06-06: en una semana partida la meta de la bolsa es proporcional a los días laborables del
/// empleado con bolsa, y si la semana mezcla bolsa y horario diario se compensa contra la meta combinada.
/// </summary>
public class SplitWeekOvertimeTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-BAG-001");
    private static readonly Shift Bag = AttendanceRegressionFixtures.CreateWeeklyOvertimeShift(40); // flexible, 40 h semanales
    private static readonly Shift Daily = AttendanceRegressionFixtures.CreateRegularShift();         // 08:00–16:00

    private static AttendanceRecalculationHarness Harness(Employee employee)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { Bag, Daily });
        h.Store.Employees.Add(employee);
        return h;
    }

    private static Employee CreateEmployee(WeekDay[] restDays, DateTime? hireDate = null) =>
        Employee.Create(Emp, "Empleado", "Bolsa", null, null, hireDate ?? new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            Bag.ShiftType, Bag.Id, restDays: restDays, overtimeAuthorized: true);

    private static void Work(AttendanceRecalculationHarness h, int dayOffset, double hours)
    {
        var day = Monday.AddDays(dayOffset);
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, day.AddHours(8), CheckType.CheckIn));
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, day.AddHours(8 + hours), CheckType.CheckIn));
    }

    private static async Task<int> WeekOvertimeAsync(AttendanceRecalculationHarness h)
    {
        await h.RecalculateAsync(Monday, Monday.AddDays(6));
        return h.Store.Dailies.Where(d => d.EmployeeId == Emp).Sum(d => d.OvertimeMinutes);
    }

    private static readonly WeekDay[] SatSun = { WeekDay.Sabado, WeekDay.Domingo };
    private static readonly WeekDay[] SundayOnly = { WeekDay.Domingo };

    [Fact]
    public async Task BagMondayToWednesday_ThenDaily_UsesTheProportionalGoal()
    {
        // 40 × 3/5 = 24 h + 2 × 8 h = 40 h de meta combinada; trabajó 30 + 20 = 50 h → 10 h.
        var employee = CreateEmployee(SatSun);
        employee.RecordAssignment(Monday.AddDays(3), Daily.Id, ShiftType.Matutino, employee.BranchId);
        var h = Harness(employee);
        for (int d = 0; d < 5; d++) Work(h, d, 10);

        (await WeekOvertimeAsync(h)).Should().Be(600, "hoy daba 4 h: la parte con bolsa se comparaba con las 40 h completas");
    }

    [Fact]
    public async Task SixDayEmployee_DividesByItsOwnWorkableDays_AndCompensatesTheShortfall()
    {
        // Descansa sólo domingo: 40 × 3/6 = 20 h + 3 × 8 h = 44 h; trabajó 3 × 6 + 3 × 10 = 48 h → 4 h (no 6 h).
        var employee = CreateEmployee(SundayOnly);
        employee.RecordAssignment(Monday.AddDays(3), Daily.Id, ShiftType.Matutino, employee.BranchId);
        var h = Harness(employee);
        for (int d = 0; d < 3; d++) Work(h, d, 6);
        for (int d = 3; d < 6; d++) Work(h, d, 10);

        (await WeekOvertimeAsync(h)).Should().Be(240, "las 2 h que faltaron en la bolsa se compensan con las extras diarias");
    }

    [Fact]
    public async Task HiredOnWednesday_TheBagGoalIsProportional()
    {
        // 40 × 3/5 = 24 h; trabajó 3 × 9 = 27 h → 3 h (hoy 0).
        var employee = CreateEmployee(SatSun, hireDate: Monday.AddDays(2));
        var h = Harness(employee);
        for (int d = 2; d < 5; d++) Work(h, d, 9);

        (await WeekOvertimeAsync(h)).Should().Be(180);
    }

    [Fact]
    public async Task FullBagWeek_IsUnchanged()
    {
        var employee = CreateEmployee(SatSun);
        var h = Harness(employee);
        for (int d = 0; d < 5; d++) Work(h, d, 9);

        (await WeekOvertimeAsync(h)).Should().Be(300, "45 h − 40 h, igual que antes");
    }

    [Fact]
    public async Task AnAbsence_DoesNotReduceTheGoal()
    {
        var employee = CreateEmployee(SatSun);
        var h = Harness(employee);
        for (int d = 0; d < 4; d++) Work(h, d, 9);

        (await WeekOvertimeAsync(h)).Should().Be(0, "36 h contra la meta completa de 40 h: la falta del viernes no la reduce");
    }
}
