using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase06;

/// <summary>
/// Cierre F06-01: la entrada de una jornada se acepta desde 2 h antes de su hora programada aunque caiga el día
/// anterior; ese marcaje pertenece a la jornada siguiente. La jornada se identifica con el día en que inicia.
/// </summary>
public class MidnightStartTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-MDN-001");

    private static AttendanceRecalculationHarness Harness(Shift shift)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Medianoche", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: Array.Empty<WeekDay>()));
        return h;
    }

    private static AttendanceRecord Punch(AttendanceRecalculationHarness h, DateTime civil)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(Emp, civil, CheckType.CheckIn);
        h.Store.Records.Add(record);
        return record;
    }

    private static DailyAttendance? Day(AttendanceRecalculationHarness h, DateTime date) =>
        h.Store.Dailies.SingleOrDefault(d => d.EmployeeId == Emp && d.Date == date.Date);

    [Theory]
    [InlineData(ShiftType.Matutino)]
    [InlineData(ShiftType.Nocturno)]
    public async Task EntryBeforeMidnight_BelongsToTheJornadaThatStartsAtMidnight(ShiftType type)
    {
        var shift = Shift.Create("00-08", TimeSpan.Zero, 15, TimeSpan.FromHours(8), type);
        var h = Harness(shift);
        Punch(h, Monday.AddMinutes(-5));              // domingo 23:55 → jornada del lunes
        Punch(h, Monday.AddHours(8));                 // lunes 08:00
        var entry = Punch(h, Tuesday.AddMinutes(-10)); // lunes 23:50 → jornada del martes
        var exit = Punch(h, Tuesday.AddHours(8));      // martes 08:00

        await h.RecalculateAsync(Monday, Tuesday);

        var tuesday = Day(h, Tuesday)!;
        tuesday.CheckInRecordId.Should().Be(entry.Id, "la entrada de las 23:50 del lunes es la anticipada del martes");
        tuesday.CheckOutRecordId.Should().Be(exit.Id);
        tuesday.LateMinutes.Should().Be(0);
        tuesday.IsAbsent.Should().BeFalse();
        tuesday.TotalWorkedMinutes.Should().Be(480);

        var monday = Day(h, Monday)!;
        monday.CheckOutRecordId.Should().NotBe(entry.Id, "la jornada del lunes no se queda con la entrada del martes");
        monday.TotalWorkedMinutes.Should().Be(480);
        monday.LateMinutes.Should().Be(0);
    }

    [Fact]
    public async Task RecalculatingOnlyTheMidnightDay_TakesItsEarlyEntryFromThePreviousDay()
    {
        var shift = Shift.Create("00-08", TimeSpan.Zero, 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = Harness(shift);
        Punch(h, Monday.AddHours(0));
        Punch(h, Monday.AddHours(8));
        var entry = Punch(h, Tuesday.AddMinutes(-10));
        Punch(h, Tuesday.AddHours(8));
        await h.RecalculateAsync(Monday, Tuesday);

        await h.RecalculateAsync(Tuesday, Tuesday);

        Day(h, Tuesday)!.CheckInRecordId.Should().Be(entry.Id);
        h.Store.Assignments.Single(a => a.Id == entry.Id).WorkDate.Should().Be(DateOnly.FromDateTime(Tuesday));
    }

    [Fact]
    public async Task WhenTheNextDayBecomesWorkable_RecalculatingItReclaimsTheEarlyEntryFromThePreviousJornada()
    {
        var afternoon = Shift.Create("14-22", new TimeSpan(14, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Vespertino);
        var midnight = Shift.Create("00-08", TimeSpan.Zero, 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { afternoon, midnight });
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Rotativo", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Rotativo, null, restDays: Array.Empty<WeekDay>()));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, afternoon.Id, isRestDay: false));
        var tuesdayRoster = ShiftRoster.Create(Emp, Tuesday, midnight.Id, isRestDay: true);
        h.Store.Rosters.Add(tuesdayRoster);
        Punch(h, Monday.AddHours(14));
        var mondayExit = Punch(h, Monday.AddHours(22));
        var lastPunch = Punch(h, Tuesday.AddMinutes(-10)); // 23:50: salida del lunes mientras el martes es descanso
        Punch(h, Tuesday.AddHours(8));
        await h.RecalculateAsync(Monday, Tuesday);
        Day(h, Monday)!.CheckOutRecordId.Should().Be(lastPunch.Id, "precondición: sin jornada el martes, las 23:50 son la salida del lunes");

        h.Store.Rosters.Remove(tuesdayRoster);
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Tuesday, midnight.Id, isRestDay: false));
        await h.RecalculateAsync(Tuesday, Tuesday);

        Day(h, Tuesday)!.CheckInRecordId.Should().Be(lastPunch.Id, "el martes reclama su entrada anticipada y el lunes se reconstruye");
        Day(h, Monday)!.CheckOutRecordId.Should().Be(mondayExit.Id);
    }

    [Fact]
    public async Task RecalculatingOnlyThePreviousDay_ExtendsToTheDayThatReceivesTheEarlyEntry()
    {
        var afternoon = Shift.Create("14-22", new TimeSpan(14, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Vespertino);
        var midnight = Shift.Create("00-08", TimeSpan.Zero, 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { afternoon, midnight });
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Rotativo", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Rotativo, null, restDays: Array.Empty<WeekDay>()));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, afternoon.Id, isRestDay: false));
        var tuesdayRoster = ShiftRoster.Create(Emp, Tuesday, midnight.Id, isRestDay: true);
        h.Store.Rosters.Add(tuesdayRoster);
        Punch(h, Monday.AddHours(14));
        Punch(h, Monday.AddHours(22));
        var lastPunch = Punch(h, Tuesday.AddMinutes(-10));
        Punch(h, Tuesday.AddHours(8));
        await h.RecalculateAsync(Monday, Tuesday);

        h.Store.Rosters.Remove(tuesdayRoster);
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Tuesday, midnight.Id, isRestDay: false));
        await h.RecalculateAsync(Monday, Monday);

        Day(h, Tuesday)!.CheckInRecordId.Should().Be(lastPunch.Id, "la unidad se extiende al martes, que recibe la entrada que el lunes libera");
    }

    [Fact]
    public async Task DayShift_FollowedByAMidnightStart_KeepsItsOwnExit()
    {
        // Lunes 14:00–22:00 (roster), martes 00:00–08:00: la salida de las 22:05 es del lunes; las 23:50, del martes.
        var afternoon = Shift.Create("14-22", new TimeSpan(14, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Vespertino);
        var midnight = Shift.Create("00-08", TimeSpan.Zero, 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { afternoon, midnight });
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Rotativo", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Rotativo, null, restDays: Array.Empty<WeekDay>()));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, afternoon.Id, isRestDay: false));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Tuesday, midnight.Id, isRestDay: false));
        Punch(h, Monday.AddHours(14));
        var mondayExit = Punch(h, Monday.AddHours(22).AddMinutes(5));
        var tuesdayEntry = Punch(h, Tuesday.AddMinutes(-10));
        Punch(h, Tuesday.AddHours(8));

        await h.RecalculateAsync(Monday, Tuesday);

        Day(h, Monday)!.CheckOutRecordId.Should().Be(mondayExit.Id);
        Day(h, Tuesday)!.CheckInRecordId.Should().Be(tuesdayEntry.Id);
    }

    [Fact]
    public async Task RegularNightShift_CrossingMidnight_IsUnchanged()
    {
        var shift = AttendanceRegressionFixtures.CreateNightShift(); // 22:00–06:00
        var h = Harness(shift);
        var entry = Punch(h, Monday.AddHours(22));
        var exit = Punch(h, Tuesday.AddHours(6));

        await h.RecalculateAsync(Monday, Tuesday);

        Day(h, Monday)!.CheckInRecordId.Should().Be(entry.Id);
        Day(h, Monday)!.CheckOutRecordId.Should().Be(exit.Id);
    }
}
