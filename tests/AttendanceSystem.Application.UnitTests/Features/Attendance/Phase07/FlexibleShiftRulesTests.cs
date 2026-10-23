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
/// Caso 6 (turno flexible, que reemplaza al continuo): salida = último marcaje de la ventana, flexible sin ventana
/// que entra de noche, marcaje solo y dobles toques como en turno fijo, faltante de la bolsa semanal, y en
/// multi-marcaje doble toque de 15 min y tramo abierto que busca su salida en el día siguiente.
/// </summary>
public class FlexibleShiftRulesTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-FLX-007");

    /// <summary>Flexible de 8 h con ventana de llegada 08:00–09:00 y 10 min de tolerancia.</summary>
    private static Shift WithWindow(PunchTrackingMode mode = PunchTrackingMode.SingleInterval) =>
        Shift.Create("Flexible 8h", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Flexible,
            flexWindowEndTime: new TimeSpan(9, 0, 0), punchTrackingMode: mode);

    private static Shift WithoutWindow(PunchTrackingMode mode = PunchTrackingMode.SingleInterval) =>
        Shift.Create("Flexible 8h libre", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Flexible,
            hasEntryWindow: false, punchTrackingMode: mode);

    private static AttendanceRecalculationHarness Harness(Shift shift, params WeekDay[] restDays)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Flexible", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: restDays.Length > 0 ? restDays : new[] { WeekDay.Domingo }, overtimeAuthorized: true));
        return h;
    }

    /// <summary>Horas relativas al lunes: 22 = lunes 22:00, 30 = martes 06:00.</summary>
    private static AttendanceRecord[] Punches(AttendanceRecalculationHarness h, params double[] hours) =>
        hours.Select(hour =>
        {
            var record = AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddMinutes(Math.Round(hour * 60)), CheckType.CheckIn);
            h.Store.Records.Add(record);
            return record;
        }).ToArray();

    private static async Task<DailyAttendance> MondayAsync(AttendanceRecalculationHarness h)
    {
        await h.RecalculateAsync(Monday, Monday.AddDays(1));
        return h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);
    }

    [Fact]
    public async Task Guard24h_WithADinnerPunch_TakesTheLastPunchOfTheWindowAsExit()
    {
        var h = Harness(AttendanceRegressionFixtures.CreateGuard24hShift(), WeekDay.Martes);
        var p = Punches(h, 7 + 50 / 60.0, 20, 21, 32 + 10 / 60.0); // cena 20:00–21:00, sale el martes 08:10

        var day = await MondayAsync(h);

        day.CheckOutRecordId.Should().Be(p[3].Id, "la salida es el último marcaje de la ventana, no el último del lunes");
        day.TemporaryExitMinutes.Should().Be(60);
    }

    [Fact]
    public async Task WithoutWindow_NightJornada_KeepsItsEveningEntry()
    {
        var h = Harness(WithoutWindow());
        var p = Punches(h, 22, 30); // lunes 22:00 → martes 06:00

        var day = await MondayAsync(h);

        day.IsAbsent.Should().BeFalse();
        day.CheckInRecordId.Should().Be(p[0].Id, "un flexible sin ventana no aparta las 22:00 para el día siguiente");
        day.CheckOutRecordId.Should().Be(p[1].Id);
        day.TotalWorkedMinutes.Should().Be(480);
        h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday.AddDays(1)).CheckInRecordId.Should().BeNull();
    }

    [Theory]
    [InlineData(17, false)]  // más cerca de la salida esperada (17:00) → salida
    [InlineData(8.5, true)]  // más cerca del fin de la ventana (09:00) → entrada
    public async Task WithWindow_LonePunch_GoesToTheNearestExpectedBoundary(double hour, bool isEntry)
    {
        var h = Harness(WithWindow());
        var p = Punches(h, hour);

        var day = await MondayAsync(h);

        if (isEntry)
        {
            day.CheckInRecordId.Should().Be(p[0].Id);
            day.MissingCheckOut.Should().BeTrue();
        }
        else
        {
            day.CheckOutRecordId.Should().Be(p[0].Id);
            day.MissingCheckIn.Should().BeTrue();
            day.LateMinutes.Should().Be(0);
        }
    }

    [Fact]
    public async Task WithoutWindow_LonePunch_IsTheEntry()
    {
        var h = Harness(WithoutWindow());
        var p = Punches(h, 17);

        var day = await MondayAsync(h);

        day.CheckInRecordId.Should().Be(p[0].Id, "sin ventana no hay hora contra la cual comparar");
    }

    [Fact]
    public async Task DoubleTap_IsOnePunch()
    {
        var h = Harness(WithWindow());
        var p = Punches(h, 8.5, 8.5 + 5 / 60.0);

        var day = await MondayAsync(h);

        day.CheckInRecordId.Should().Be(p[0].Id);
        day.CheckOutRecordId.Should().BeNull();
        day.EarlyDepartureMinutes.Should().Be(0, "08:30 y 08:35 no son una jornada de 5 min");
    }

    [Fact]
    public async Task WeeklyBag_FinishedWeekBelowTheGoal_ReportsTheShortfall()
    {
        var h = Harness(AttendanceRegressionFixtures.CreateWeeklyOvertimeShift(40), WeekDay.Sabado, WeekDay.Domingo);
        for (int d = 0; d < 4; d++) Punches(h, d * 24 + 8, d * 24 + 17); // 4 × 9 h = 36 h

        await h.RecalculateAsync(Monday, Monday.AddDays(6));

        var week = h.Store.Dailies.Where(d => d.EmployeeId == Emp).ToList();
        week.Sum(d => d.WeeklyShortfallMinutes).Should().Be(240);
        week.Should().ContainSingle(d => d.Notes.Contains("Faltaron 240 min para la meta semanal"));
    }

    [Fact]
    public async Task WeeklyBag_UnfinishedWeek_ReportsNoShortfallYet()
    {
        var h = Harness(AttendanceRegressionFixtures.CreateWeeklyOvertimeShift(40), WeekDay.Sabado, WeekDay.Domingo);
        Punches(h, 8, 17);

        await h.RecalculateAsync(Monday, Monday.AddDays(2));

        h.Store.Dailies.Where(d => d.EmployeeId == Emp).Sum(d => d.WeeklyShortfallMinutes).Should().Be(0);
    }

    [Fact]
    public async Task MultiInterval_PunchesWithinFifteenMinutes_AreADoubleTap()
    {
        var h = Harness(WithWindow(PunchTrackingMode.MultiInterval));
        Punches(h, 8, 8 + 2 / 60.0, 12, 13, 17);

        var day = await MondayAsync(h);

        day.TotalWorkedMinutes.Should().Be(480, "08:02 es un doble toque y no recorre las parejas");
        day.MissingCheckOut.Should().BeFalse();
    }

    [Fact]
    public async Task MultiInterval_OpenLastInterval_LooksForItsExitTheNextDay()
    {
        var h = Harness(WithoutWindow(PunchTrackingMode.MultiInterval));
        var p = Punches(h, 22, 26, 26.5, 30); // 22:00–02:00 y 02:30–06:00

        var day = await MondayAsync(h);

        day.CheckOutRecordId.Should().Be(p[3].Id);
        day.TotalWorkedMinutes.Should().Be(450);
        day.MissingCheckOut.Should().BeFalse();
    }

    [Fact]
    public async Task MultiInterval_ClosedDay_DoesNotTakeNextDayPunches()
    {
        var h = Harness(WithoutWindow(PunchTrackingMode.MultiInterval));
        Punches(h, 8, 12, 13, 17, 24 + 2);

        var day = await MondayAsync(h);

        day.TotalWorkedMinutes.Should().Be(480, "sólo un tramo abierto busca su salida en el día siguiente");
    }
}
