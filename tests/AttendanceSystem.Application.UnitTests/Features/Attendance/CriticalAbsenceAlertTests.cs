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
}
