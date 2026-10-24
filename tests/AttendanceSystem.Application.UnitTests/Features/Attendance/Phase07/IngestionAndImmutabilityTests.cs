using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Queries.GetUnmatchedPunches;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 14: aviso de marcajes que no cuentan (IDs no registrados, antes del ingreso, después de la baja), marcajes
/// inmutables (anulados y corregidos) y toque doble configurable.
/// </summary>
public class IngestionAndImmutabilityTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly Shift Fixed = Shift.Create("Fijo 9-17", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);

    private static Employee NewEmployee(string id, DateTime hireDate) =>
        Employee.Create(EmployeeId.From(id), "Empleado", id, null, null, hireDate, Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            Fixed.ShiftType, Fixed.Id, restDays: new[] { WeekDay.Domingo });

    private static void Punch(AttendanceRecalculationHarness h, string id, DateTime civil) =>
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(EmployeeId.From(id), civil, CheckType.CheckIn));

    private static Task<System.Collections.Generic.IReadOnlyList<UnmatchedPunchDto>> UnmatchedAsync(AttendanceRecalculationHarness h)
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => h.Store.Employees.ToList());
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetAllDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Device>());
        return new GetUnmatchedPunchesQueryHandler(h.AttendanceRepository, employees.Object, devices.Object)
            .Handle(new GetUnmatchedPunchesQuery(DateOnly.FromDateTime(Monday.AddDays(-10)), DateOnly.FromDateTime(Monday.AddDays(10))), CancellationToken.None);
    }

    [Fact]
    public async Task PunchesThatDoNotCount_AreListedByIdAndReason()
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Employees.Add(NewEmployee("ACTIVO", new DateTime(2024, 1, 1)));
        h.Store.Employees.Add(NewEmployee("NUEVO", Monday.AddDays(2)));
        var gone = NewEmployee("BAJA", new DateTime(2024, 1, 1));
        gone.Terminate(Monday.AddDays(-1));
        h.Store.Employees.Add(gone);

        Punch(h, "ACTIVO", Monday.AddHours(9));
        Punch(h, "999", Monday.AddHours(9));
        Punch(h, "999", Monday.AddDays(1).AddHours(9));
        Punch(h, "NUEVO", Monday.AddHours(9));
        Punch(h, "BAJA", Monday.AddHours(9));

        var rows = await UnmatchedAsync(h);

        rows.Select(r => (r.EmployeeId, r.Kind, r.Count)).Should().Equal(
            ("999", UnmatchedPunchKind.NotRegistered, 2),
            ("NUEVO", UnmatchedPunchKind.BeforeHire, 1),
            ("BAJA", UnmatchedPunchKind.AfterTermination, 1));
        rows.Single(r => r.EmployeeId == "BAJA").Detail.Should().Be($"Siguió checando después de su baja del {Monday.AddDays(-1):dd/MM/yyyy}.");
        rows.Single(r => r.EmployeeId == "999").EmployeeName.Should().BeNull();
    }

    [Fact]
    public async Task VoidedPunches_DoNotRaiseTheWarning()
    {
        var h = new AttendanceRecalculationHarness();
        Punch(h, "999", Monday.AddHours(9));
        h.Store.Records.Single().Void("admin", "Prueba de reloj");

        (await UnmatchedAsync(h)).Should().BeEmpty();
    }

    [Fact]
    public void ARecordCannotBeVoidedTwice_AndRequiresAReason()
    {
        var record = AttendanceRegressionFixtures.CreatePunch(EmployeeId.From("E1"), Monday.AddHours(9), CheckType.CheckIn);

        record.Invoking(r => r.Void("admin", " ")).Should().Throw<DomainException>();
        record.Void("admin", "Duplicado");
        record.Invoking(r => r.Void("admin", "Otra vez")).Should().Throw<DomainException>();
    }

    [Fact]
    public void ACorrection_KeepsTheOriginal_AndPointsBothWays()
    {
        var original = AttendanceRegressionFixtures.CreatePunch(EmployeeId.From("E1"), Monday.AddHours(8), CheckType.CheckIn);
        var correction = PunchTimeCorrection.Create(original.DeviceId, DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Monday), null, 60, "Reloj atrasado", "admin");

        var corrected = correction.Apply(original, original.CheckTime.AddHours(1), Monday.AddHours(9), DateOnly.FromDateTime(Monday), original.TimeZoneId!);

        original.CheckTime.Should().Be(corrected.CheckTime.AddHours(-1), "el original no cambia");
        original.IsVoided.Should().BeTrue();
        original.ReplacedByRecordId.Should().Be(corrected.Id);
        corrected.ReplacesRecordId.Should().Be(original.Id);
        corrected.DeviceId.Should().Be(original.DeviceId);
        correction.Entries.Single().NewRecordId.Should().Be(corrected.Id);
    }

    [Fact]
    public void ACorrectedPunch_TakesOverTheDecisionsOfTheOriginal()
    {
        var original = AttendanceRecordId.CreateNew();
        var replacement = AttendanceRecordId.CreateNew();
        var adjustment = AttendanceAdjustment.Create(EmployeeId.From("E1"), Monday, "rh");
        adjustment.AssignPosition(AttendancePosition.CheckOut, original, Monday.AddHours(23), "rh");
        adjustment.ClassifyExit(original.Value, TemporaryExitStatus.ApprovedPaid, "rh");

        adjustment.ReplaceRecord(original, replacement, Monday.AddHours(24), "admin").Should().BeTrue();

        adjustment.OverrideRecord(AttendancePosition.CheckOut).Should().Be(replacement);
        adjustment.OverrideTime(AttendancePosition.CheckOut).Should().Be(Monday.AddHours(24));
        adjustment.ExitClassifications.Should().ContainKey(replacement.Value).And.NotContainKey(original.Value);
        adjustment.ReplaceRecord(original, replacement, Monday, "admin").Should().BeFalse();
    }

    [Theory]
    [InlineData(15, true)]   // la segunda checada a los 20 min queda como intermedia sin pareja
    [InlineData(30, false)]  // con 30 min es un toque doble
    public async Task TheDoubleTapWindow_IsConfigurable(int doubleTapMinutes, bool expectsIssue)
    {
        var configuration = SystemConfiguration.CreateDefault();
        configuration.UpdateDoubleTapMinutes(doubleTapMinutes);
        var h = new AttendanceRecalculationHarness { Configuration = configuration };
        h.Store.Shifts.Add(Fixed);
        h.Store.Employees.Add(NewEmployee("E1", new DateTime(2024, 1, 1)));
        Punch(h, "E1", Monday.AddHours(9));
        Punch(h, "E1", Monday.AddHours(9).AddMinutes(20));
        Punch(h, "E1", Monday.AddHours(17));

        await h.RecalculateAsync(Monday, Monday);

        var day = h.Store.Dailies.Single(d => d.Date == Monday);
        (day.CalculationIssue != null).Should().Be(expectsIssue, day.CalculationIssue ?? "sin incidencia");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void TheDoubleTapWindow_StaysWithinLimits(int minutes) =>
        SystemConfiguration.CreateDefault().Invoking(c => c.UpdateDoubleTapMinutes(minutes)).Should().Throw<DomainException>();
}
