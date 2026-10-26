using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Consolidation;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.Features.Attendance.PeriodClosure;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 17: un periodo cerrado congela su asistencia (no se recalcula ni acepta ajustes); se cierra a mano o solo
/// 3 días después de terminar, en orden; sólo el último cerrado se reabre, con motivo.
/// </summary>
public class PeriodClosureTests
{
    // Configuración por defecto: semana de lunes a domingo.
    private static readonly DateTime Today = new(2026, 8, 20); // jueves
    private static readonly DateTime WeekOf3 = new(2026, 8, 3);   // lunes
    private static readonly DateTime WeekOf10 = new(2026, 8, 10); // lunes

    private sealed class FixedToday : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Today.AddHours(1), DateTimeKind.Utc));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (PeriodClosureHandlers Handlers, InMemoryPeriodClosureRepository Closures) Handlers()
    {
        var closures = new InMemoryPeriodClosureRepository();
        var configuration = new Mock<ISystemConfigurationRepository>();
        configuration.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SystemConfiguration.CreateDefault());
        var handlers = new PeriodClosureHandlers(closures, configuration.Object, Mock.Of<IDailyAttendanceRepository>(), Mock.Of<IUnitOfWork>(),
            Mock.Of<ISender>(), NullLogger<PeriodClosureHandlers>.Instance, timeProvider: new FixedToday());
        return (handlers, closures);
    }

    [Fact]
    public async Task APeriodIsClosedOnlyWhenItEnded_AndInOrder()
    {
        var (handlers, closures) = Handlers();

        (await handlers.Handle(new ClosePeriodCommand(Today.AddDays(-3), Today.AddDays(3)), CancellationToken.None)).Error.Should().Be("El periodo aún no termina.");
        (await handlers.Handle(new ClosePeriodCommand(WeekOf3, WeekOf3.AddDays(5)), CancellationToken.None)).Error.Should().Contain("no corresponden");

        (await handlers.Handle(new ClosePeriodCommand(WeekOf3, WeekOf3.AddDays(6)), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handlers.Handle(new ClosePeriodCommand(WeekOf3, WeekOf3.AddDays(6)), CancellationToken.None)).Error.Should().Be("El periodo ya está cerrado.");
        (await handlers.Handle(new ClosePeriodCommand(WeekOf3.AddDays(-14), WeekOf3.AddDays(-8)), CancellationToken.None)).Error.Should().Contain("anterior al último cerrado");
        (await handlers.Handle(new ClosePeriodCommand(WeekOf10, WeekOf10.AddDays(6)), CancellationToken.None)).IsSuccess.Should().BeTrue();

        closures.Items.Should().HaveCount(2).And.OnlyContain(c => c.IsActive && !c.Automatic);
    }

    [Fact]
    public async Task OnlyTheLastClosedPeriodIsReopened_WithAReason()
    {
        var (handlers, closures) = Handlers();
        await handlers.Handle(new ClosePeriodCommand(WeekOf3, WeekOf3.AddDays(6)), CancellationToken.None);
        await handlers.Handle(new ClosePeriodCommand(WeekOf10, WeekOf10.AddDays(6)), CancellationToken.None);
        var first = closures.Items.Single(c => c.StartDate == WeekOf3);
        var last = closures.Items.Single(c => c.StartDate == WeekOf10);

        (await handlers.Handle(new ReopenPeriodCommand(first.Id, "Corrección"), CancellationToken.None)).Error.Should().Contain("último");
        (await handlers.Handle(new ReopenPeriodCommand(last.Id, " "), CancellationToken.None)).IsFailure.Should().BeTrue();
        (await handlers.Handle(new ReopenPeriodCommand(last.Id, "Faltó un marcaje"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        last.IsActive.Should().BeFalse();
        last.ReopenReason.Should().Be("Faltó un marcaje");
        closures.Items.Should().HaveCount(2, "un cierre nunca se borra");
    }

    [Fact]
    public async Task PeriodsCloseByThemselves_ThreeDaysAfterEnding_ButNotAfterBeingReopened()
    {
        var (handlers, closures) = Handlers();

        // Hoy jueves 20: la semana del 10 al 16 terminó hace 4 días; la del 17 sigue en curso.
        (await handlers.Handle(new AutoClosePeriodsCommand(), CancellationToken.None)).Should().Be(1);
        closures.Items.Single().Should().Match<AttendancePeriodClosure>(c => c.StartDate == WeekOf10 && c.Automatic);

        await handlers.Handle(new ReopenPeriodCommand(closures.Items.Single().Id, "Revisión"), CancellationToken.None);
        (await handlers.Handle(new AutoClosePeriodsCommand(), CancellationToken.None)).Should().Be(0, "un periodo reabierto se cierra a mano");
    }

    [Fact]
    public async Task AClosedDay_IsNotRecalculated_NorAcceptsDecisions()
    {
        var shift = Shift.Create("Fijo 9-17", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var emp = EmployeeId.From("EMP-CLOSE-017");
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(emp, "Empleado", "Cierre", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Domingo }));
        await h.RecalculateAsync(WeekOf3, WeekOf3);
        h.Store.Dailies.Single().IsAbsent.Should().BeTrue();
        var closedDaily = h.Store.Dailies.Single();

        h.ClosureRepository.Items.Add(AttendancePeriodClosure.Close(WeekOf3, WeekOf3.AddDays(6), "rh", automatic: false));
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(emp, WeekOf3.AddHours(9), CheckType.CheckIn));
        await h.RecalculateAsync(WeekOf3, WeekOf3.AddDays(7));

        h.Store.Dailies.Should().Contain(closedDaily, "la jornada cerrada se queda como estaba");
        closedDaily.IsAbsent.Should().BeTrue();
        h.Store.Dailies.Should().Contain(d => d.Date == WeekOf3.AddDays(7), "el primer día abierto sí se calcula");

        var decision = await h.Decisions().DecideAsync(emp, WeekOf3, "Descanso", null, null,
            (c, a) => { a.SetRestDayOverride(true, c.Author); return Task.FromResult(AttendanceSystem.Application.Common.Result.Success()); }, CancellationToken.None);
        decision.Error.Should().Be($"El periodo del {WeekOf3:dd/MM/yyyy} al {WeekOf3.AddDays(6):dd/MM/yyyy} está cerrado: no se puede modificar la asistencia de ese día.");
    }

    [Fact]
    public async Task ARetroactiveChange_StartsRecalculatingAtTheFirstOpenDay()
    {
        var closures = new InMemoryPeriodClosureRepository();
        closures.Items.Add(AttendancePeriodClosure.Close(WeekOf3, WeekOf3.AddDays(6), "rh", automatic: false));
        var service = new PeriodClosureService(closures);

        (await service.OpenRangeAsync(WeekOf3.AddDays(2), WeekOf10.AddDays(3), CancellationToken.None)).Should().Be((WeekOf10, WeekOf10.AddDays(3)));
        (await service.OpenRangeAsync(WeekOf3, WeekOf3.AddDays(6), CancellationToken.None)).Should().BeNull();

        var impact = new RecalculationImpactDto(3, 4, WeekOf10, WeekOf10.AddDays(3), WeekOf3.AddDays(6));
        impact.Describe().Should().EndWith($"Los días hasta el {WeekOf3.AddDays(6):dd/MM/yyyy} están en periodos cerrados y no se recalculan.");
    }
}
