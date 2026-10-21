using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Shifts.Commands.CreateShift;
using AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase06;

/// <summary>
/// Decisión 5: el turno mixto es una plantilla semanal; cada día usa un horario existente con todas sus reglas.
/// </summary>
public class MixedShiftTemplateTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-MIX-001");

    private static readonly Shift Morning = AttendanceRegressionFixtures.CreateRegularShift(); // 08:00–16:00
    private static readonly Shift Night = AttendanceRegressionFixtures.CreateNightShift();     // 22:00–06:00
    private static readonly Shift Split = Shift.Create("Partido 8-12 y 16-20", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
        secondBlockStartTime: new TimeSpan(16, 0, 0), secondBlockEndTime: new TimeSpan(20, 0, 0), secondBlockToleranceMinutes: 10);

    /// <summary>Lunes matutino, martes nocturno, jueves partido; miércoles y resto sin horario (descanso).</summary>
    private static (AttendanceRecalculationHarness H, Shift Mixed) Scenario()
    {
        var mixed = Shift.Create("Mixto semanal", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Mixto, new[]
        {
            ShiftDay.ForShift(DayOfWeek.Monday, Morning),
            ShiftDay.ForShift(DayOfWeek.Tuesday, Night),
            ShiftDay.ForShift(DayOfWeek.Thursday, Split)
        });
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { Morning, Night, Split, mixed });
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Mixto", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Mixto, mixed.Id, restDays: Array.Empty<WeekDay>(), overtimeAuthorized: true));
        return (h, mixed);
    }

    private static AttendanceRecord Punch(AttendanceRecalculationHarness h, DateTime civil)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(Emp, civil, CheckType.CheckIn);
        h.Store.Records.Add(record);
        return record;
    }

    private static DailyAttendance? Day(AttendanceRecalculationHarness h, DateTime date) =>
        h.Store.Dailies.SingleOrDefault(d => d.EmployeeId == Emp && d.Date == date.Date);

    [Fact]
    public async Task EachDay_IsCalculatedWithItsOwnSchedule()
    {
        var (h, _) = Scenario();
        var thursday = Monday.AddDays(3);
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(16));
        Punch(h, Monday.AddDays(1).AddHours(22));
        var nightExit = Punch(h, Monday.AddDays(2).AddHours(6));
        Punch(h, thursday.AddHours(8));
        var b1Out = Punch(h, thursday.AddHours(12));
        var b2In = Punch(h, thursday.AddHours(16).AddMinutes(5));
        Punch(h, thursday.AddHours(20));

        await h.RecalculateAsync(Monday, thursday);

        Day(h, Monday)!.ShiftId.Should().Be(Morning.Id, "la jornada registra el horario del día");
        Day(h, Monday)!.TotalWorkedMinutes.Should().Be(480);

        var tuesday = Day(h, Monday.AddDays(1))!;
        tuesday.ShiftId.Should().Be(Night.Id);
        tuesday.CheckOutRecordId.Should().Be(nightExit.Id, "el nocturno del martes toma su salida del miércoles");

        var split = Day(h, thursday)!;
        split.ShiftId.Should().Be(Split.Id);
        split.Block1CheckOutRecordId.Should().Be(b1Out.Id, "el partido calcula sus dos bloques");
        split.Block2CheckInRecordId.Should().Be(b2In.Id);
        split.CalculationIssue.Should().BeNull("el horario partido está completo");
    }

    [Fact]
    public async Task DayWithoutSchedule_IsRest()
    {
        var (h, _) = Scenario();
        var wednesday = Monday.AddDays(2);

        await h.RecalculateAsync(wednesday, wednesday);

        var day = Day(h, wednesday)!;
        day.IsRestDay.Should().BeTrue();
        day.IsAbsent.Should().BeFalse("un día de descanso de la plantilla no es falta");
    }

    [Fact]
    public async Task LegacyDay_WithHoursPerDay_IsStillCalculated_AndAsksForASchedule()
    {
        var legacy = AttendanceRegressionFixtures.CreateMixedShift(); // formato anterior: horas por día
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(legacy);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Mixto", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Mixto, legacy.Id, restDays: Array.Empty<WeekDay>()));
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(16));

        await h.RecalculateAsync(Monday, Monday);

        var day = Day(h, Monday)!;
        day.TotalWorkedMinutes.Should().Be(480);
        day.CalculationIssue.Should().Contain("formato anterior").And.Contain("lunes");
    }

    [Fact]
    public void ShiftDay_RejectsMixedOrRotatingSchedules()
    {
        var rotating = Shift.Create("Rotativo", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Rotativo);
        var act = () => ShiftDay.ForShift(DayOfWeek.Monday, rotating);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task CreateMixed_RequiresAnExistingScheduleForEachConfiguredDay()
    {
        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.GetByIdAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Morning);
        var handler = new CreateShiftCommandHandler(repo.Object, Mock.Of<IUnitOfWork>());

        var withoutSchedule = await handler.Handle(new CreateShiftCommand("Mixto", TimeSpan.Zero, 0, TimeSpan.FromHours(8), ShiftType.Mixto,
            new[] { new ShiftDayDto(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, ShiftType.Matutino) }), CancellationToken.None);
        withoutSchedule.IsFailure.Should().BeTrue();
        withoutSchedule.Error.Should().Contain("Seleccione el horario del lunes");

        var valid = await handler.Handle(new CreateShiftCommand("Mixto", TimeSpan.Zero, 0, TimeSpan.FromHours(8), ShiftType.Mixto,
            new[] { new ShiftDayDto(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, ShiftType.Matutino, Morning.Id.Value) }), CancellationToken.None);
        valid.IsSuccess.Should().BeTrue(valid.IsSuccess ? string.Empty : valid.Error);
        repo.Verify(r => r.AddAsync(It.Is<Shift>(s => s.Days.Single().DayShiftId == Morning.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingAScheduleUsedByAMixedShift_IsRejected()
    {
        var (_, mixed) = Scenario();
        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.GetByIdAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Morning);
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Morning, mixed });
        var employees = new Mock<IEmployeeRepository>();
        var handler = new DeleteShiftCommandHandler(repo.Object, employees.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new DeleteShiftCommand(Morning.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Mixto semanal");
        repo.Verify(r => r.Delete(It.IsAny<Shift>()), Times.Never);
    }
}
