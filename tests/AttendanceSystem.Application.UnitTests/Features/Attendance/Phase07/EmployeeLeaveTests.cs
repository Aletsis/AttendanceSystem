using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.Features.Attendance.Queries.GetUnmatchedPunches;
using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 16: vacaciones, incapacidad y baja temporal son períodos del estado del empleado. En esos días no se calcula
/// asistencia ni aparece en reportes; al terminar regresa solo a Alta.
/// </summary>
public class EmployeeLeaveTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-LEAVE-016");
    private static readonly Shift Fixed = Shift.Create("Fijo 9-17", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);

    private static Employee NewEmployee() =>
        Employee.Create(Emp, "Empleado", "Vacaciones", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            Fixed.ShiftType, Fixed.Id, restDays: new[] { WeekDay.Domingo });

    // ---------- Dominio ----------

    [Fact]
    public void ALeave_PausesTheEmployee_OnlyDuringItsDates()
    {
        var employee = NewEmployee();
        employee.StartLeave(EmployeeStatus.Vacaciones, Monday, Monday.AddDays(4));

        employee.IsActiveOn(Monday.AddDays(-1)).Should().BeTrue();
        employee.IsActiveOn(Monday).Should().BeFalse();
        employee.IsActiveOn(Monday.AddDays(4)).Should().BeFalse();
        employee.IsActiveOn(Monday.AddDays(5)).Should().BeTrue();
        employee.IsEmployedOn(Monday.AddDays(2)).Should().BeTrue("sigue teniendo relación laboral");
        employee.StatusOn(Monday.AddDays(2)).Should().Be(EmployeeStatus.Vacaciones);
        employee.IsActiveDuring(Monday, Monday.AddDays(4)).Should().BeFalse();
        employee.IsActiveDuring(Monday, Monday.AddDays(5)).Should().BeTrue();
    }

    [Fact]
    public void WhenTheLeaveEnds_TheStatusReturnsToAltaByItself()
    {
        var employee = NewEmployee();
        employee.StartLeave(EmployeeStatus.Incapacidad, DateTime.Today.AddDays(-3), DateTime.Today.AddDays(1));
        employee.Status.Should().Be(EmployeeStatus.Incapacidad);

        employee.RefreshStatus(DateTime.Today.AddDays(2)).Should().BeTrue();
        employee.Status.Should().Be(EmployeeStatus.Alta);
    }

    [Fact]
    public void AnOpenLeave_LastsUntilTheEmployeeReturnsToAlta()
    {
        var employee = NewEmployee();
        employee.StartLeave(EmployeeStatus.BajaTemporal, Monday, null);
        employee.IsActiveOn(Monday.AddDays(60)).Should().BeFalse();

        employee.EndLeave(Monday.AddDays(10));

        employee.IsActiveOn(Monday.AddDays(9)).Should().BeFalse();
        employee.IsActiveOn(Monday.AddDays(10)).Should().BeTrue();
        employee.StatusPeriods.Single().EndDate.Should().Be(Monday.AddDays(9));
    }

    [Fact]
    public void AnOverlappingLeave_ReplacesOnlyTheOverlappedDays()
    {
        var employee = NewEmployee();
        employee.StartLeave(EmployeeStatus.Vacaciones, Monday, Monday.AddDays(13));

        employee.StartLeave(EmployeeStatus.Incapacidad, Monday.AddDays(3), Monday.AddDays(5));

        employee.StatusOn(Monday.AddDays(2)).Should().Be(EmployeeStatus.Vacaciones);
        employee.StatusOn(Monday.AddDays(4)).Should().Be(EmployeeStatus.Incapacidad);
        employee.StatusOn(Monday.AddDays(6)).Should().Be(EmployeeStatus.Vacaciones);
        employee.StatusOn(Monday.AddDays(13)).Should().Be(EmployeeStatus.Vacaciones);
        employee.StatusOn(Monday.AddDays(14)).Should().Be(EmployeeStatus.Alta);
    }

    [Fact]
    public void ALeave_RequiresAnActiveEmployment_AndATerminationCutsIt()
    {
        var employee = NewEmployee();
        employee.Invoking(e => e.StartLeave(EmployeeStatus.Vacaciones, new DateTime(2023, 6, 1), null)).Should().Throw<DomainException>();
        employee.Invoking(e => e.StartLeave(EmployeeStatus.Vacaciones, Monday, Monday.AddDays(-1))).Should().Throw<DomainException>();

        employee.StartLeave(EmployeeStatus.Vacaciones, Monday, Monday.AddDays(10));
        employee.Terminate(Monday.AddDays(4));

        employee.StatusPeriods.Single().EndDate.Should().Be(Monday.AddDays(4));
        employee.StatusOn(Monday.AddDays(6)).Should().Be(EmployeeStatus.Baja);
    }

    // ---------- Cálculo ----------

    private static AttendanceRecalculationHarness Week(Employee employee)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(Fixed);
        h.Store.Employees.Add(employee);
        return h;
    }

    [Fact]
    public async Task OnLeaveDays_NothingIsCalculated_NorShownAsAbsence()
    {
        var employee = NewEmployee();
        employee.StartLeave(EmployeeStatus.Vacaciones, Monday.AddDays(1), Monday.AddDays(2));
        var h = Week(employee);

        await h.RecalculateAsync(Monday, Monday.AddDays(3));

        h.Store.Dailies.Select(d => d.Date).Should().BeEquivalentTo(new[] { Monday, Monday.AddDays(3) });
        h.Store.Dailies.Should().OnlyContain(d => d.IsAbsent, "lunes y jueves sin checar sí son faltas");
    }

    [Fact]
    public async Task ALeaveRegisteredLate_RemovesTheDaysAlreadyCalculated()
    {
        var employee = NewEmployee();
        var h = Week(employee);
        await h.RecalculateAsync(Monday, Monday.AddDays(4));
        h.Store.Dailies.Should().HaveCount(5);

        employee.StartLeave(EmployeeStatus.Incapacidad, Monday.AddDays(1), Monday.AddDays(3));
        await h.RecalculateAsync(Monday, Monday.AddDays(4));

        h.Store.Dailies.Select(d => d.Date).Should().BeEquivalentTo(new[] { Monday, Monday.AddDays(4) });
    }

    [Fact]
    public async Task PunchesDuringALeave_AreFlaggedForReview()
    {
        var employee = NewEmployee();
        employee.StartLeave(EmployeeStatus.Vacaciones, Monday, Monday.AddDays(4));
        var h = Week(employee);
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddDays(2).AddHours(9), CheckType.CheckIn));
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => h.Store.Employees.ToList());
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetAllDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Device>());

        var rows = await new GetUnmatchedPunchesQueryHandler(h.AttendanceRepository, employees.Object, devices.Object)
            .Handle(new GetUnmatchedPunchesQuery(DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Monday.AddDays(6))), CancellationToken.None);

        rows.Should().ContainSingle(r => r.EmployeeId == Emp.Value && r.Kind == UnmatchedPunchKind.DuringLeave);
        rows.Single().Detail.Should().Be($"Checó durante su periodo de vacaciones del {Monday:dd/MM/yyyy} al {Monday.AddDays(4):dd/MM/yyyy}.");
    }

    // ---------- Permisos ----------

    private sealed class FakePermissions : IPermissionService
    {
        private readonly HashSet<string> _granted;
        public FakePermissions(params string[] granted) => _granted = granted.ToHashSet();
        public bool IsSystem => false;
        public bool Has(string permission) => _granted.Contains(permission);
    }

    private static UpdateEmployeeCommand WithStatus(Employee e, EmployeeStatus status, DateTime? to = null) => new(
        e.Id.Value, e.FirstName, e.LastName, e.Email, e.PhoneNumber, e.HireDate, e.Gender, status,
        e.BranchId.Value.ToString(), e.DepartmentId.Value.ToString(), e.PositionId.Value.ToString(),
        e.ShiftType, e.ScheduleId!.Value.ToString(), null, e.OvertimeAuthorized, e.OvertimeCalculationMethod, e.OvertimeCapType,
        e.OvertimeCapMinutes, e.CalculateOvertimeBeforeEntry, e.CardNumber, e.DevicePassword, e.Photo, e.DevicePrivilege,
        e.GetEffectiveRestDays().Select(d => (int)d).ToList(), Monday, to);

    [Fact]
    public void OnlyHrAndAdministrators_ManageLeaves_WhileAnyoneTerminatesOrRehires()
    {
        var user = new FakePermissions(Permissions.EmployeesEdit);
        var hr = new FakePermissions(Permissions.EmployeesEdit, Permissions.EmployeesLeave);
        var employee = NewEmployee();

        EmployeeFieldPermissions.ForUpdate(user, employee, WithStatus(employee, EmployeeStatus.Vacaciones, Monday.AddDays(4))).Should().NotBeNull();
        EmployeeFieldPermissions.ForUpdate(hr, employee, WithStatus(employee, EmployeeStatus.Vacaciones, Monday.AddDays(4))).Should().BeNull();
        EmployeeFieldPermissions.ForUpdate(user, employee, WithStatus(employee, EmployeeStatus.Baja)).Should().BeNull();

        employee.StartLeave(EmployeeStatus.Incapacidad, DateTime.Today.AddDays(-1), null);
        EmployeeFieldPermissions.ForUpdate(user, employee, WithStatus(employee, EmployeeStatus.Alta)).Should().NotBeNull("terminar una incapacidad es de RH");
    }
}
