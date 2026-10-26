using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Application.Features.Roster.Commands.ExtendRotationPatterns;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;
using AttendanceSystem.Application.Features.Shifts.Commands.CheckScheduleIntegrity;
using AttendanceSystem.Application.Features.Shifts.Commands.DeleteShift;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 10 (sin turno): en lugar de calcular bien un día sin turno, se impide que exista. Ningún empleado se guarda sin
/// horario, un horario en uso no se borra, el roster de un esquema indefinido se extiende solo, y la revisión de
/// horarios reporta lo que quede como alertas. Si aun así un día se calcula sin turno, lleva nota y no genera extras.
/// </summary>
public class NoShiftPreventionTests
{
    private static readonly DateTime Today = new(2026, 8, 3);
    private static readonly Shift Morning = Shift.Create("Matutino 8-16", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
    private static readonly Shift Evening = Shift.Create("Vespertino 16-24", new TimeSpan(16, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);

    private static Employee NewEmployee(string id, ShiftType? type, ShiftId? scheduleId = null) =>
        Employee.Create(EmployeeId.From(id), "Ana", "López", null, null, new DateTime(2024, 1, 1), Gender.Female,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            type, scheduleId, restDays: new[] { WeekDay.Domingo }, overtimeAuthorized: true);

    // ---------- 1. Alta sin horario ----------

    [Theory]
    [InlineData(null, false, "Seleccione el turno")]
    [InlineData(ShiftType.Matutino, false, "Seleccione el horario")]
    [InlineData(ShiftType.Rotativo, false, null)]  // el rotativo sigue su roster
    [InlineData(ShiftType.Matutino, true, null)]
    public async Task CreatingAnEmployee_RequiresShiftTypeAndSchedule(ShiftType? type, bool withSchedule, string? expectedError)
    {
        var branch = Branch.Create("A01", "Matriz", "Av. 1");
        var department = Department.Create("Operación", "Ops");
        var position = Position.Create("Operador", "Op", 1m);
        department.AddPosition(position);
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetByIdAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var departments = new Mock<IDepartmentRepository>();
        departments.Setup(r => r.GetByIdAsync(department.Id, It.IsAny<CancellationToken>())).ReturnsAsync(department);
        var positions = new Mock<IPositionRepository>();
        positions.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.GetByIdAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Morning);
        var handler = new CreateEmployeeCommandHandler(new Mock<IEmployeeRepository>().Object, branches.Object, departments.Object,
            positions.Object, shifts.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ILogger<CreateEmployeeCommandHandler>>());

        var result = await handler.Handle(new CreateEmployeeCommand("EMP-NEW", "Ana", "López", null, null, new DateTime(2025, 1, 1), Gender.Female,
            branch.Id.Value.ToString(), department.Id.Value.ToString(), position.Id.Value.ToString(),
            type, withSchedule ? Morning.Id.Value.ToString() : null, null, false, OvertimeCalculationMethod.NoRounding, OvertimeCapType.None, null, false),
            CancellationToken.None);

        if (expectedError == null) result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        else result.Error.Should().Contain(expectedError);
    }

    // ---------- 3. Borrar un horario en uso ----------

    [Theory]
    [InlineData("employee", "asignado a uno o más empleados")]
    [InlineData("roster", "3 día(s) del roster rotativo")]
    [InlineData("adjustment", "1 ajuste(s) manuales de RH")]
    public async Task DeletingAScheduleInUse_IsRejected(string usedBy, string expected)
    {
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.GetByIdAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Morning);
        shifts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Morning });
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.IsShiftInUseAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(usedBy == "employee");
        var rosters = new Mock<IShiftRosterRepository>();
        rosters.Setup(r => r.CountByShiftAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(usedBy == "roster" ? 3 : 0);
        var adjustments = new Mock<IAttendanceAdjustmentRepository>();
        adjustments.Setup(r => r.CountByShiftOverrideAsync(Morning.Id, It.IsAny<CancellationToken>())).ReturnsAsync(usedBy == "adjustment" ? 1 : 0);
        var handler = new DeleteShiftCommandHandler(shifts.Object, employees.Object, Mock.Of<IUnitOfWork>(), rosters.Object, adjustments.Object);

        var result = await handler.Handle(new DeleteShiftCommand(Morning.Id), CancellationToken.None);

        result.Error.Should().Contain(expected);
        shifts.Verify(r => r.Delete(It.IsAny<Shift>()), Times.Never);
    }

    // ---------- 4a / 5. Patrón rotativo ----------

    [Theory]
    [InlineData(RotationSchemeType.Rotativo3x8)]
    [InlineData(RotationSchemeType.Esquema4x3)]
    [InlineData(RotationSchemeType.Personalizado)]
    public async Task APatternWithoutShifts_IsRejected_InsteadOfGeneratingOnlyRestDays(RotationSchemeType scheme)
    {
        var handler = new GenerateRotationPatternCommandHandler(new Mock<IShiftRosterRepository>().Object, Mock.Of<IUnitOfWork>());

        var act = () => handler.Handle(new GenerateRotationPatternCommand(new List<string> { "EMP-ROT" }, Today, Today.AddDays(30), scheme,
            new List<Guid>(), CustomSlots: new List<CustomRotationSlotDto> { new(null, true) }), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AnIndefinitePattern_IsSaved_AndExtendedContinuingItsCycle()
    {
        var employee = NewEmployee("EMP-ROT", ShiftType.Rotativo);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var stored = new List<ShiftRoster>();
        var rosters = new Mock<IShiftRosterRepository>();
        rosters.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ShiftRoster>());
        rosters.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((added, _) => stored.AddRange(added));
        RotationPattern? saved = null;
        var patterns = new Mock<IRotationPatternRepository>();
        patterns.Setup(r => r.AddAsync(It.IsAny<RotationPattern>(), It.IsAny<CancellationToken>()))
            .Callback<RotationPattern, CancellationToken>((p, _) => saved = p);
        patterns.Setup(r => r.GetIndefiniteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { saved! });

        // 2x8: una semana matutino, una vespertino; generado sólo 10 días.
        var generate = new GenerateRotationPatternCommandHandler(rosters.Object, employees.Object, Mock.Of<IUnitOfWork>(), null, null, patterns.Object);
        await generate.Handle(new GenerateRotationPatternCommand(new List<string> { "EMP-ROT" }, Today, Today.AddDays(9), RotationSchemeType.Rotativo2x8,
            new List<Guid> { Morning.Id.Value, Evening.Id.Value }, DaysPerShift: 7, IsIndefinite: true, RestDayMode: RotationRestDayMode.FixedDaysOfWeek,
            FixedRestDays: new List<DayOfWeek> { DayOfWeek.Sunday }), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.IsIndefinite.Should().BeTrue();
        saved.GeneratedThrough.Should().Be(Today.AddDays(9));

        var extend = new ExtendRotationPatternsCommandHandler(patterns.Object, rosters.Object, employees.Object, Mock.Of<IUnitOfWork>());
        await extend.Handle(new ExtendRotationPatternsCommand(Today), CancellationToken.None);

        saved.GeneratedThrough.Should().Be(Today.AddDays(ExtendRotationPatternsCommandHandler.HorizonDays));
        stored.Select(r => r.Date).Should().OnlyHaveUniqueItems().And.HaveCount(ExtendRotationPatternsCommandHandler.HorizonDays + 1);
        // El día 14 (lunes de la tercera semana) vuelve al matutino: el ciclo sigue desde el inicio original.
        stored.Single(r => r.Date == Today.AddDays(14)).ShiftId.Should().Be(Morning.Id);
        stored.Single(r => r.Date == Today.AddDays(10)).ShiftId.Should().Be(Evening.Id);
        stored.Single(r => r.Date == Today.AddDays(13)).IsRestDay.Should().BeTrue("domingo");
    }

    [Fact]
    public async Task AnEmployeeWhoStopsRotating_EndsThePatternExtension()
    {
        var employee = NewEmployee("EMP-ROT", ShiftType.Rotativo);
        employee.RecordAssignment(Today.AddDays(20), Morning.Id, ShiftType.Matutino, employee.BranchId);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var stored = new List<ShiftRoster>();
        var rosters = new Mock<IShiftRosterRepository>();
        rosters.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ShiftRoster>());
        rosters.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((added, _) => stored.AddRange(added));
        var definition = new RotationPatternDefinition(RotationSchemeType.Esquema4x3, new List<Guid> { Morning.Id.Value }, 7, 0, null,
            RotationRestDayMode.AtEndOfCycle, null).Serialize();
        var pattern = RotationPattern.Create(employee.Id, Today, null, definition, Today.AddDays(9));
        var patterns = new Mock<IRotationPatternRepository>();
        patterns.Setup(r => r.GetIndefiniteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { pattern });

        await new ExtendRotationPatternsCommandHandler(patterns.Object, rosters.Object, employees.Object, Mock.Of<IUnitOfWork>())
            .Handle(new ExtendRotationPatternsCommand(Today), CancellationToken.None);

        stored.Max(r => r.Date).Should().Be(Today.AddDays(19));
        pattern.EndDate.Should().Be(Today.AddDays(19));
    }

    // ---------- 4b / 6. Revisión de horarios ----------

    private sealed class IntegrityScenario
    {
        public List<Employee> Employees { get; } = new();
        public List<Shift> Shifts { get; } = new() { Morning, Evening };
        public List<ShiftRoster> Rosters { get; } = new();
        public List<DailyAttendance> Dailies { get; } = new();
        public List<SystemAlert> Alerts { get; } = new();

        public async Task<IReadOnlyList<string>> RunAsync(DateTime? today = null)
        {
            var employees = new Mock<IEmployeeRepository>();
            employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Employees);
            var shifts = new Mock<IShiftRepository>();
            shifts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Shifts);
            var rosters = new Mock<IShiftRosterRepository>();
            rosters.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EmployeeId id, DateTime from, DateTime to, CancellationToken _) =>
                    Rosters.Where(r => r.EmployeeId == id && r.Date >= from && r.Date <= to).ToList());
            var dailies = new Mock<IDailyAttendanceRepository>();
            dailies.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateTime from, DateTime to, BranchId? _, EmployeeId? _, CancellationToken _) =>
                    Dailies.Where(d => d.Date >= from && d.Date <= to).ToList());
            var alerts = new Mock<ISystemAlertRepository>();
            alerts.Setup(r => r.GetActiveAlertsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Alerts.Where(a => !a.IsResolved).ToList());
            alerts.Setup(r => r.AddAsync(It.IsAny<SystemAlert>(), It.IsAny<CancellationToken>()))
                .Callback<SystemAlert, CancellationToken>((a, _) => Alerts.Add(a));

            var handler = new CheckScheduleIntegrityCommandHandler(employees.Object, shifts.Object, rosters.Object, dailies.Object, alerts.Object, Mock.Of<IUnitOfWork>());
            var result = await handler.Handle(new CheckScheduleIntegrityCommand(today ?? Today), CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
            return Alerts.Where(a => !a.IsResolved).Select(a => a.Message).ToList();
        }
    }

    [Fact]
    public async Task AnEmployeeWithoutSchedule_IsReported_AndTheAlertResolvesWhenFixed()
    {
        var scenario = new IntegrityScenario();
        var employee = NewEmployee("EMP-LEG", ShiftType.Matutino);
        scenario.Employees.Add(employee);

        (await scenario.RunAsync()).Should().ContainSingle(m => m.Contains("EMP-LEG") && m.Contains("no tiene horario"));

        employee.RecordAssignment(new DateTime(2024, 1, 1), Morning.Id, ShiftType.Matutino, employee.BranchId);
        (await scenario.RunAsync()).Should().BeEmpty();
        scenario.Alerts.Should().ContainSingle().Which.IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task AFutureAssignmentWithoutSchedule_IsReported()
    {
        var scenario = new IntegrityScenario();
        var employee = NewEmployee("EMP-FUT", ShiftType.Matutino, Morning.Id);
        employee.RecordAssignment(Today.AddDays(10), null, ShiftType.Vespertino, employee.BranchId);
        scenario.Employees.Add(employee);

        (await scenario.RunAsync()).Should().ContainSingle(m => m.Contains($"a partir del {Today.AddDays(10):dd/MM/yyyy}"));
    }

    [Fact]
    public async Task ARosterAboutToEnd_IsReportedFifteenDaysAhead()
    {
        var scenario = new IntegrityScenario();
        var employee = NewEmployee("EMP-ROT", ShiftType.Rotativo);
        scenario.Employees.Add(employee);
        for (int d = -31; d <= 9; d++) scenario.Rosters.Add(ShiftRoster.Create(employee.Id, Today.AddDays(d), Morning.Id, false));

        (await scenario.RunAsync()).Should().ContainSingle(m => m.Contains($"termina el {Today.AddDays(9):dd/MM/yyyy}"));

        for (int d = 10; d <= 20; d++) scenario.Rosters.Add(ShiftRoster.Create(employee.Id, Today.AddDays(d), null, true));
        (await scenario.RunAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AGapInTheRoster_IsReported()
    {
        var scenario = new IntegrityScenario();
        var employee = NewEmployee("EMP-ROT", ShiftType.Rotativo);
        scenario.Employees.Add(employee);
        for (int d = -31; d <= 20; d++)
            if (d != -2) scenario.Rosters.Add(ShiftRoster.Create(employee.Id, Today.AddDays(d), Morning.Id, false));

        (await scenario.RunAsync()).Should().ContainSingle(m => m.Contains("1 día(s)") && m.Contains(Today.AddDays(-2).ToString("dd/MM/yyyy")));
    }

    [Fact]
    public async Task AMixedTemplateWithAnEmptyDay_IsReported()
    {
        var scenario = new IntegrityScenario();
        var mixed = Shift.Create("Mixto viejo", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Mixto,
            days: MixedShiftTemplateDays(DayOfWeek.Sunday));
        scenario.Shifts.Add(mixed);

        (await scenario.RunAsync()).Should().ContainSingle(m => m.Contains("Mixto viejo") && m.Contains("domingo"));
    }

    [Fact]
    public async Task DaysCalculatedWithoutShift_AreReported()
    {
        var scenario = new IntegrityScenario();
        var employee = NewEmployee("EMP-NS", ShiftType.Matutino, Morning.Id);
        scenario.Employees.Add(employee);
        scenario.Dailies.Add(DailyAttendance.Create(employee.Id, Today.AddDays(-3), null, Today.AddDays(-3).AddHours(8), Today.AddDays(-3).AddHours(17)));
        scenario.Dailies.Add(DailyAttendance.Create(employee.Id, Today.AddDays(-1), null, null, null, isRestDay: true)); // descanso sin marcajes: no cuenta

        (await scenario.RunAsync()).Should().ContainSingle(m => m.Contains("EMP-NS") && m.Contains("1 jornada(s) calculadas sin turno"));
    }

    private static IEnumerable<ShiftDay> MixedShiftTemplateDays(DayOfWeek missing) =>
        Enum.GetValues<DayOfWeek>().Where(d => d != missing).Select(d => ShiftDay.ForShift(d, Morning)).ToList();

    // ---------- Respaldo: un día calculado sin turno ----------

    [Fact]
    public async Task ADayCalculatedWithoutShift_HasANote_AndNoOvertime()
    {
        var h = new AttendanceRecalculationHarness();
        var emp = NewEmployee("EMP-NS", null);
        h.Store.Employees.Add(emp);
        foreach (var hour in new[] { 8, 19 })
            h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(emp.Id, Today.AddHours(hour), CheckType.CheckIn));

        await h.RecalculateAsync(Today, Today);

        var day = h.Store.Dailies.Single(d => d.EmployeeId == emp.Id && d.Date == Today);
        day.ShiftId.Should().BeNull();
        day.TotalWorkedMinutes.Should().Be(660);
        day.OvertimeMinutes.Should().Be(0, "sin turno no hay meta contra la cual calcular extras");
        day.Notes.Should().Contain("Sin turno asignado: asígnele un horario");
    }
}
