using System;
using System.Linq;
using System.Threading.Tasks;
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

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 11 en cada tipo de turno: una salida pendiente se descuenta y, si no se repone al final, es salida anticipada
/// (en el partido, al final del bloque 2); en bolsa semanal reduce la bolsa sin salida anticipada diaria.
/// </summary>
public class TemporaryExitShiftTypesTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-TMP-011");

    private static AttendanceRecalculationHarness Harness(Shift shift, bool windowEnabled = true)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Salidas", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Domingo }, overtimeAuthorized: true));
        var config = SystemConfiguration.CreateDefault();
        config.SetLunchWindowEnabled(windowEnabled);
        h.Configuration = config;
        return h;
    }

    /// <summary>Horas relativas al lunes: 25.5 = martes 01:30.</summary>
    private static async Task<DailyAttendance> CalculateAsync(AttendanceRecalculationHarness h, params double[] hours)
    {
        foreach (var hour in hours)
            h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddMinutes(Math.Round(hour * 60)), CheckType.CheckIn));
        await h.RecalculateAsync(Monday, Monday.AddDays(1));
        return h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);
    }

    [Theory]
    [InlineData(19, 450, 30)]
    [InlineData(19.5, 480, 0)]
    public async Task Split_PendingExitInABlock_IsMadeUpAtTheEndOfBlock2(double exit, int worked, int early)
    {
        var shift = Shift.Create("Partido 9-13 y 15-19", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(15, 0, 0), secondBlockEndTime: new TimeSpan(19, 0, 0), secondBlockToleranceMinutes: 10);

        var day = await CalculateAsync(Harness(shift), 9, 10, 10.5, 13, 15, exit);

        day.TemporaryExits.Should().ContainSingle(e => e.Status == TemporaryExitStatus.Pending && !e.IsAutomaticLunch,
            "el partido no tiene comida automática");
        day.TotalWorkedMinutes.Should().Be(worked);
        day.EarlyDepartureMinutes.Should().Be(early);
    }

    [Fact]
    public async Task Night_WithTheWindowOff_TheMidnightMealIsAPendingExit()
    {
        var shift = Shift.Create("Nocturno 22-06", new TimeSpan(22, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Nocturno);

        var day = await CalculateAsync(Harness(shift, windowEnabled: false), 22, 25, 25.5, 30);

        day.TemporaryExits.Single().Status.Should().Be(TemporaryExitStatus.Pending);
        day.TotalWorkedMinutes.Should().Be(450);
        day.EarlyDepartureMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Night_WithTheWindowOn_TheMidnightMealIsLunch()
    {
        var shift = Shift.Create("Nocturno 22-06", new TimeSpan(22, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Nocturno);

        var day = await CalculateAsync(Harness(shift), 22, 25, 25.5, 30);

        day.LunchMinutesTaken.Should().Be(30);
        day.TotalWorkedMinutes.Should().Be(480);
        day.EarlyDepartureMinutes.Should().Be(0);
    }

    [Fact]
    public async Task FlexibleWeeklyBag_PendingExitReducesTheWorkedTime_WithoutDailyEarlyDeparture()
    {
        var day = await CalculateAsync(Harness(AttendanceRegressionFixtures.CreateWeeklyOvertimeShift(40)), 8, 10, 11, 17);

        day.TotalWorkedMinutes.Should().Be(480);
        day.EarlyDepartureMinutes.Should().Be(0);
        day.TemporaryExits.Single().Status.Should().Be(TemporaryExitStatus.Pending);
    }
}
