using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>La alerta de puesto crítico usa la misma resolución de turno y vigencia que el cálculo.</summary>
public class CriticalAbsenceAlertTests
{
    [Fact]
    public async Task Alert_RespectsMixedTemplateRestDays_AndEmploymentPeriods()
    {
        var localNow = await LocalNowAsync();
        if (localNow.TimeOfDay < TimeSpan.FromMinutes(16)) return; // la entrada de las 00:00 aún está en tolerancia

        var position = Position.Create("Crítico", null, 1000m, isCritical: true);
        var midnight = Shift.Create("Turno 00:00", TimeSpan.Zero, 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        // Plantilla mixta con horario sólo mañana: hoy es descanso.
        var mixed = Shift.Create("Mixto", TimeSpan.Zero, 10, TimeSpan.FromHours(8), ShiftType.Mixto,
            new[] { ShiftDay.ForShift(localNow.AddDays(1).DayOfWeek, midnight) });

        Employee Create(string id, Shift schedule) => Employee.Create(EmployeeId.From(id), "E", id, null, null, new DateTime(2024, 1, 1), Gender.Male,
            BranchId.CreateNew(), DepartmentId.CreateNew(), position.Id, schedule.ShiftType, schedule.Id, restDays: Array.Empty<WeekDay>());
        var working = Create("EMP-CRIT-1", midnight);
        var restingByTemplate = Create("EMP-CRIT-2", mixed);
        var terminated = Create("EMP-CRIT-3", midnight);
        terminated.Terminate(localNow.Date.AddDays(-3));

        var positions = new Mock<IPositionRepository>();
        positions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position> { position });
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { working, restingByTemplate, terminated });
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { midnight, mixed });
        var attendance = new Mock<IAttendanceRepository>();
        var alerts = new Mock<ISystemAlertRepository>();
        var email = new Mock<IEmailService>();

        var handler = new CheckCriticalAbsencesCommandHandler(Mock.Of<IUnitOfWork>(), email.Object, positions.Object, employees.Object,
            attendance.Object, shifts.Object, alerts.Object);

        var result = await handler.Handle(new CheckCriticalAbsencesCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        result.Value.Should().Be(1, "sólo el empleado activo con horario hoy genera alerta");
        email.Verify(e => e.SendAlertAsync(It.IsAny<string>(), It.Is<string>(m => m.Contains("EMP-CRIT-1")), It.IsAny<AlertLevel>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<DateTime> LocalNowAsync()
    {
        var tz = await DefaultAuthoritativeTimeZoneResolver.Instance.ResolveForBranchAsync(null, CancellationToken.None);
        return DefaultTimeNormalizer.Instance.ToCivilTime(DateTime.UtcNow, tz);
    }

    // Auditoría de tareas, bloque D. La zona por omisión es America/Mexico_City (UTC-6).
    private sealed class FixedTime(DateTime civil) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(civil.AddHours(6), DateTimeKind.Utc));
    }

    private sealed class Harness
    {
        public readonly Position Position = Position.Create("Vigilancia", null, 1000m, isCritical: true);
        public readonly List<Employee> Employees = new();
        public readonly List<Shift> Shifts = new();
        public readonly List<SystemAlert> Alerts = new();
        public readonly List<string> Sent = new();
        public readonly HashSet<(string Employee, DateTime Date)> CheckIns = new();
        public readonly HashSet<string> FailingEmail = new();
        public readonly Mock<IUnitOfWork> UnitOfWork = new();
        public readonly List<string> Calls = new();

        public Employee Add(string id, TimeSpan start, string firstName = "E")
        {
            var shift = Shift.Create($"Turno {id}", start, 10, TimeSpan.FromHours(8), ShiftType.Matutino);
            Shifts.Add(shift);
            var employee = Employee.Create(EmployeeId.From(id), firstName, id, null, null, new DateTime(2024, 1, 1), Gender.Male,
                BranchId.CreateNew(), DepartmentId.CreateNew(), Position.Id, shift.ShiftType, shift.Id, restDays: Array.Empty<WeekDay>());
            Employees.Add(employee);
            return employee;
        }

        public Task<AttendanceSystem.Application.Common.Result<int>> RunAtAsync(DateTime civil)
        {
            var positions = new Mock<IPositionRepository>();
            positions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position> { Position });
            var employees = new Mock<IEmployeeRepository>();
            employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Employees);
            var shifts = new Mock<IShiftRepository>();
            shifts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Shifts);
            var attendance = new Mock<IAttendanceRepository>();
            attendance.Setup(r => r.HasPunchForDateAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EmployeeId e, DateTime d, CancellationToken _) => CheckIns.Contains((e.Value, d.Date)));
            var alerts = new Mock<ISystemAlertRepository>();
            alerts.Setup(r => r.ExistsAsync(It.IsAny<AlertType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AlertType t, string key, CancellationToken _) => Alerts.Any(a => a.Type == t && a.ReferenceId == key));
            alerts.Setup(r => r.AddAsync(It.IsAny<SystemAlert>(), It.IsAny<CancellationToken>()))
                .Callback<SystemAlert, CancellationToken>((a, _) => { Alerts.Add(a); Calls.Add("add"); })
                .Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => Calls.Add("save")).ReturnsAsync(1);
            var email = new Mock<IEmailService>();
            email.Setup(e => e.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>(), It.IsAny<CancellationToken>()))
                .Returns((string _, string body, AlertLevel _, CancellationToken _) =>
                {
                    Calls.Add("email");
                    if (FailingEmail.Any(body.Contains)) throw new InvalidOperationException("SMTP caído");
                    Sent.Add(body);
                    return Task.CompletedTask;
                });

            return new CheckCriticalAbsencesCommandHandler(UnitOfWork.Object, email.Object, positions.Object, employees.Object,
                attendance.Object, shifts.Object, alerts.Object, timeProvider: new FixedTime(civil))
                .Handle(new CheckCriticalAbsencesCommand(), CancellationToken.None);
        }
    }

    private static readonly DateTime Day = new(2026, 10, 14);

    [Fact]
    public async Task AFailedEmail_DoesNotStopTheOthers_AndOnlyTheFailedOneIsRetried()
    {
        var h = new Harness();
        h.Add("CRIT-A", new TimeSpan(8, 0, 0));
        h.Add("CRIT-B", new TimeSpan(8, 0, 0));
        h.FailingEmail.Add("CRIT-A");

        var first = await h.RunAtAsync(Day.AddHours(9));

        first.IsSuccess.Should().BeFalse("el fallo del envío se reporta");
        h.Sent.Should().ContainSingle(b => b.Contains("CRIT-B"));
        h.Alerts.Should().ContainSingle(a => a.ReferenceId == "CRIT-B_20261014");

        h.FailingEmail.Clear();
        var second = await h.RunAtAsync(Day.AddHours(9.25));

        second.Value.Should().Be(1);
        h.Sent.Should().HaveCount(2);
        h.Sent[1].Should().Contain("CRIT-A");
    }

    [Fact]
    public async Task EachAlert_IsRecordedRightAfterItIsSent()
    {
        var h = new Harness();
        h.Add("CRIT-C", new TimeSpan(8, 0, 0));
        h.Add("CRIT-D", new TimeSpan(8, 0, 0));

        await h.RunAtAsync(Day.AddHours(9));

        h.Calls.Should().Equal("email", "add", "save", "email", "add", "save");
    }

    [Fact]
    public async Task AShiftStartingAt2350_IsAlertedAfterMidnight_UnderItsOwnDay()
    {
        var h = new Harness();
        h.Add("CRIT-N", new TimeSpan(23, 50, 0));

        // La jornada del 13 (entrada 23:50 del 13) ya se avisó; la del 14 aún está en tolerancia.
        h.CheckIns.Add(("CRIT-N", Day.AddDays(-1)));
        (await h.RunAtAsync(Day.Add(new TimeSpan(23, 55, 0)))).Value.Should().Be(0, "aún está en tolerancia");
        (await h.RunAtAsync(Day.AddDays(1).Add(new TimeSpan(0, 10, 0)))).Value.Should().Be(1);
        (await h.RunAtAsync(Day.AddDays(1).Add(new TimeSpan(0, 25, 0)))).Value.Should().Be(0);

        h.Alerts.Should().ContainSingle().Which.ReferenceId.Should().Be("CRIT-N_20261014");
        h.Sent.Single().Should().Contain("14/10/2026");
    }

    [Fact]
    public async Task ALateNightShift_WithAnEntryAfterMidnight_IsNotAlerted()
    {
        var h = new Harness();
        h.Add("CRIT-M", new TimeSpan(23, 50, 0));
        h.CheckIns.Add(("CRIT-M", Day.AddDays(1)));

        (await h.RunAtAsync(Day.AddDays(1).Add(new TimeSpan(0, 10, 0)))).Value.Should().Be(0);
    }

    [Fact]
    public async Task YesterdaysOrdinaryShift_IsNotAlertedAgainToday()
    {
        var h = new Harness();
        h.Add("CRIT-Y", new TimeSpan(8, 0, 0));

        // Hoy a las 07:00 su entrada de hoy aún no llega; la de ayer ya se revisó ayer.
        (await h.RunAtAsync(Day.AddHours(7))).Value.Should().Be(0);
        h.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheEmailBody_EscapesHtml_ButTheRecordedAlertKeepsTheText()
    {
        var h = new Harness();
        h.Add("CRIT-H", new TimeSpan(8, 0, 0), firstName: "<b>Ana</b>");

        await h.RunAtAsync(Day.AddHours(9));

        h.Sent.Single().Should().Contain("&lt;b&gt;Ana&lt;/b&gt;").And.NotContain("<b>");
        h.Alerts.Single().Message.Should().Contain("<b>Ana</b>");
    }
}
