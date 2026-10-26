using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Consolidation;
using AttendanceSystem.Application.Features.Roster.Commands.AssignShiftRoster;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 15: los cambios de plantilla de horario, configuración y roster con vigencia de hoy o pasada recalculan lo ya
/// calculado; al iniciar el día se calcula el día anterior de todos los activos (faltas incluidas).
/// </summary>
public class RetroactiveRecalculationTests
{
    private static readonly DateTime Today = new(2026, 8, 10);

    private sealed class FixedToday : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Today.AddHours(0.5), DateTimeKind.Utc));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly Shift Morning = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
    private static readonly Shift Evening = Shift.Create("Vespertino", new TimeSpan(14, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);
    private static readonly Shift Mixed = Shift.Create("Mixto", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Mixto,
        new[] { ShiftDay.ForShift(DayOfWeek.Monday, Morning), ShiftDay.ForShift(DayOfWeek.Tuesday, Evening) });

    private static Employee NewEmployee(string id, Shift shift, DateTime? terminated = null)
    {
        var employee = Employee.Create(EmployeeId.From(id), "Empleado", id, null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Domingo });
        if (terminated is { } date) employee.Terminate(date);
        return employee;
    }

    private static (AttendanceRecalculationHarness H, RetroactiveRecalculation Recalculation) Setup(params Employee[] employees)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { Morning, Evening, Mixed });
        h.Store.Employees.AddRange(employees);
        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => h.Store.Employees.ToList());
        var shiftRepo = new Mock<IShiftRepository>();
        shiftRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => h.Store.Shifts.ToList());
        var rosterRepo = new Mock<IShiftRosterRepository>();
        rosterRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => h.Store.Rosters.ToList());
        return (h, new RetroactiveRecalculation(employeeRepo.Object, h.DailyRepository, rosterRepo.Object, shiftRepo.Object,
            h.Consolidations, NoOpConsolidationWakeup.Instance, new FixedToday()));
    }

    [Fact]
    public async Task EditingAShiftFromAPastDate_RecalculatesEveryoneWhoUsesIt_UntilToday()
    {
        var (h, recalculation) = Setup(
            NewEmployee("DIRECTO", Morning),
            NewEmployee("MIXTO", Mixed),
            NewEmployee("OTRO", Evening));
        h.Store.Rosters.Add(ShiftRoster.Create(EmployeeId.From("ROSTER"), Today.AddDays(-2), Morning.Id, false));

        var impact = await recalculation.RequestForShiftAsync(Morning.Id, Today.AddDays(-5), CancellationToken.None);

        impact.Employees.Should().Be(3);
        impact.Days.Should().Be(6);
        h.Store.ConsolidationRequests.Select(r => r.EmployeeId.Value).Should().BeEquivalentTo("DIRECTO", "MIXTO", "ROSTER");
        h.Store.ConsolidationRequests.Should().OnlyContain(r => r.Start == Today.AddDays(-5) && r.End == Today);
    }

    [Fact]
    public async Task AFutureEffectiveDate_RecalculatesNothing()
    {
        var (h, recalculation) = Setup(NewEmployee("DIRECTO", Morning));

        var impact = await recalculation.RequestForShiftAsync(Morning.Id, Today.AddDays(1), CancellationToken.None);

        impact.IsEmpty.Should().BeTrue();
        h.Store.ConsolidationRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AConfigurationChange_RecalculatesActiveEmployeesFromTheEffectiveDate()
    {
        var (h, recalculation) = Setup(NewEmployee("ACTIVO", Morning), NewEmployee("BAJA", Morning, terminated: new DateTime(2025, 1, 1)));

        var impact = await recalculation.RequestForAllEmployeesAsync(Today, CancellationToken.None);

        impact.Describe().Should().Be($"Se recalcularán 1 empleado(s), 1 día(s) del {Today:dd/MM/yyyy} al {Today:dd/MM/yyyy}.");
        h.Store.ConsolidationRequests.Should().ContainSingle(r => r.EmployeeId.Value == "ACTIVO" && r.Start == Today && r.End == Today);
    }

    [Fact]
    public async Task AtTheStartOfTheDay_YesterdayIsCalculatedForEveryActiveEmployee()
    {
        var (h, _) = Setup(NewEmployee("ACTIVO", Morning), NewEmployee("BAJA", Morning, terminated: new DateTime(2025, 1, 1)));
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => h.Store.Employees.ToList());

        var requested = await new CalculatePreviousDayCommandHandler(employees.Object, h.Consolidations, NoOpConsolidationWakeup.Instance,
            NullLogger<CalculatePreviousDayCommandHandler>.Instance, new FixedToday()).Handle(new CalculatePreviousDayCommand(), CancellationToken.None);

        requested.Should().Be(1);
        h.Store.ConsolidationRequests.Should().ContainSingle(r => r.EmployeeId.Value == "ACTIVO" && r.Start == Today.AddDays(-1) && r.End == Today.AddDays(-1));
    }

    [Theory]
    [InlineData(-3, true)]
    [InlineData(5, false)]
    public async Task ChangingTheRosterOfAPastDay_RecalculatesThatDay(int offsetDays, bool recalculates)
    {
        var h = new AttendanceRecalculationHarness();
        var roster = new Mock<IShiftRosterRepository>();
        var date = DateTime.Today.AddDays(offsetDays);

        await new AssignShiftRosterCommandHandler(roster.Object, h.UnitOfWork, h.Consolidations, NoOpConsolidationWakeup.Instance)
            .Handle(new AssignShiftRosterCommand("ROT-1", date, Morning.Id.Value, false), CancellationToken.None);

        h.Store.ConsolidationRequests.Any(r => r.EmployeeId.Value == "ROT-1" && r.Start == date.Date && r.End == date.Date).Should().Be(recalculates);
    }
}
