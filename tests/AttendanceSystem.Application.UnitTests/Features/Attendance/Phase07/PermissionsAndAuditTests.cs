using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Authorization;
using AttendanceSystem.Application.Behaviors;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.Features.Employees.Commands;
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
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase07;

/// <summary>
/// Caso 13: permisos por rol (Usuario, RH, Administrador) en comandos y campos, motivo obligatorio en marcajes manuales
/// y en asignar/desasignar, borrado de marcajes manuales, regreso al roster e historial de la jornada.
/// </summary>
public class PermissionsAndAuditTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-PERM-013");

    // ---------- Matriz de roles ----------

    [Fact]
    public void Administrator_HasEveryPermission() =>
        Permissions.All.Should().OnlyContain(p => RolePermissions.Grants(new[] { AppRoles.Administrator }, p));

    [Theory]
    [InlineData(Permissions.EmployeesCreate, true)]
    [InlineData(Permissions.EmployeesEdit, true)]
    [InlineData(Permissions.EmployeesImport, true)]
    [InlineData(Permissions.EmployeesSendToDevice, true)]
    [InlineData(Permissions.ExternalEmployeesCreate, true)]
    [InlineData(Permissions.DevicesDownload, true)]
    [InlineData(Permissions.ClockAlertsView, true)]
    [InlineData(Permissions.ReportsView, true)]
    [InlineData(Permissions.EmployeesEditSchedule, false)]
    [InlineData(Permissions.EmployeesEditOvertime, false)]
    [InlineData(Permissions.EmployeesDelete, false)]
    [InlineData(Permissions.EmployeesDevicePrivilege, false)]
    [InlineData(Permissions.ExternalEmployeesEdit, false)]
    [InlineData(Permissions.PositionsManage, false)]
    [InlineData(Permissions.RosterManage, false)]
    [InlineData(Permissions.DevicesForceFullDownload, false)]
    [InlineData(Permissions.AttendanceCalculate, false)]
    [InlineData(Permissions.AttendanceAdjust, false)]
    [InlineData(Permissions.AttendanceManualPunch, false)]
    [InlineData(Permissions.AttendanceViewHistory, false)]
    [InlineData(Permissions.ReportsPayrollExport, false)]
    public void User_ReadsRegistersEmployeesAndMakesMinorCorrections(string permission, bool granted) =>
        RolePermissions.Grants(new[] { AppRoles.User }, permission).Should().Be(granted);

    [Theory]
    [InlineData(Permissions.EmployeesEditSchedule, true)]
    [InlineData(Permissions.EmployeesEditOvertime, true)]
    [InlineData(Permissions.ExternalEmployeesEdit, true)]
    [InlineData(Permissions.PositionsManage, true)]
    [InlineData(Permissions.RosterManage, true)]
    [InlineData(Permissions.AttendanceCalculate, true)]
    [InlineData(Permissions.AttendanceAdjust, true)]
    [InlineData(Permissions.AttendanceManualPunch, true)]
    [InlineData(Permissions.AttendanceViewHistory, true)]
    [InlineData(Permissions.ReportsPayrollExport, true)]
    [InlineData(Permissions.EmployeesDelete, false)]
    [InlineData(Permissions.EmployeesDevicePrivilege, false)]
    [InlineData(Permissions.ExternalEmployeesDelete, false)]
    [InlineData(Permissions.BranchesManage, false)]
    [InlineData(Permissions.DepartmentsManage, false)]
    [InlineData(Permissions.PositionsDelete, false)]
    [InlineData(Permissions.ShiftsManage, false)]
    [InlineData(Permissions.DevicesManage, false)]
    [InlineData(Permissions.DevicesDownloadAll, false)]
    [InlineData(Permissions.ClockAlertsResolve, false)]
    [InlineData(Permissions.AttendanceDeleteManualPunch, false)]
    [InlineData(Permissions.SettingsManage, false)]
    [InlineData(Permissions.BackupsManage, false)]
    [InlineData(Permissions.UsersManage, false)]
    [InlineData(Permissions.JobsView, false)]
    public void HumanResources_HasEverythingButSystemConfiguration(string permission, bool granted) =>
        RolePermissions.Grants(new[] { AppRoles.HumanResources }, permission).Should().Be(granted);

    // ---------- Filtro de autorización de MediatR ----------

    private sealed class FakePermissions : IPermissionService
    {
        private readonly HashSet<string> _granted;
        public FakePermissions(params string[] granted) => _granted = granted.ToHashSet();
        public bool IsSystem => false;
        public bool Has(string permission) => _granted.Contains(permission);
    }

    [Fact]
    public async Task ACommandWithoutPermission_FailsWithoutRunning()
    {
        var behavior = new AuthorizationBehavior<DeleteEmployeeCommand, Result>(new FakePermissions(Permissions.EmployeesEdit));
        var ran = false;

        var result = await behavior.Handle(new DeleteEmployeeCommand("E1"), _ => { ran = true; return Task.FromResult(Result.Success()); }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PermissionServiceExtensions.DeniedMessage);
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task ARequestWithoutResult_ThrowsForbidden()
    {
        var behavior = new AuthorizationBehavior<GetAttendanceHistoryQuery, IReadOnlyList<AttendanceHistoryEntryDto>>(new FakePermissions());

        var act = () => behavior.Handle(new GetAttendanceHistoryQuery("E1", DateOnly.FromDateTime(Monday)),
            _ => Task.FromResult<IReadOnlyList<AttendanceHistoryEntryDto>>(Array.Empty<AttendanceHistoryEntryDto>()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task WhatAnAuthorizedCommandSends_IsPartOfTheSameOperation()
    {
        // Un usuario descarga un reloj; la descarga recalcula la asistencia aunque él no pueda calcular.
        var permissions = new FakePermissions(Permissions.DevicesDownload);
        var outer = new AuthorizationBehavior<DownloadFromDeviceCommand, Result<DownloadResultDto>>(permissions);
        var inner = new AuthorizationBehavior<DeleteEmployeeCommand, Result>(permissions);
        Result? nested = null;

        await outer.Handle(new DownloadFromDeviceCommand("DEV"), async _ =>
        {
            nested = await inner.Handle(new DeleteEmployeeCommand("E1"), _ => Task.FromResult(Result.Success()), CancellationToken.None);
            return Result<DownloadResultDto>.Success(null!);
        }, CancellationToken.None);

        nested!.IsSuccess.Should().BeTrue();
        (await inner.Handle(new DeleteEmployeeCommand("E1"), _ => Task.FromResult(Result.Success()), CancellationToken.None))
            .IsFailure.Should().BeTrue("fuera de la operación autorizada se vuelve a revisar");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OnlyTheAdministrator_ReDownloadsTheWholeDevice(bool forceFullSync, bool allowed)
    {
        var behavior = new AuthorizationBehavior<DownloadFromDeviceCommand, Result<DownloadResultDto>>(new FakePermissions(Permissions.DevicesDownload));

        var result = await behavior.Handle(new DownloadFromDeviceCommand("DEV", ForceFullSync: forceFullSync),
            _ => Task.FromResult(Result<DownloadResultDto>.Success(null!)), CancellationToken.None);

        result.IsSuccess.Should().Be(allowed);
    }

    [Fact]
    public void WithoutASignedInUser_TheSystemRunsUnrestricted()
    {
        new PermissionService(null).Has(Permissions.SettingsManage).Should().BeTrue();

        var anonymous = new Mock<ICurrentUserService>();
        new PermissionService(anonymous.Object).Has(Permissions.SettingsManage).Should().BeTrue();

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.Roles).Returns(new[] { AppRoles.User });
        new PermissionService(user.Object).Has(Permissions.SettingsManage).Should().BeFalse();
        new PermissionService(user.Object).Has(Permissions.EmployeesCreate).Should().BeTrue();
    }

    // ---------- Permisos por campo del empleado ----------

    private static Employee NewEmployee(DevicePrivilege privilege = DevicePrivilege.User) =>
        Employee.Create(Emp, "Empleado", "Permisos", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Matutino, ShiftId.From(Guid.Parse("11111111-1111-1111-1111-111111111111")), restDays: new[] { WeekDay.Domingo },
            overtimeAuthorized: true, overtimeCalculationMethod: OvertimeCalculationMethod.RoundByHalfHour, devicePrivilege: privilege);

    private static UpdateEmployeeCommand SameAs(Employee e) => new(
        e.Id.Value, e.FirstName, e.LastName, e.Email, e.PhoneNumber, e.HireDate, e.Gender, e.Status,
        e.BranchId.Value.ToString(), e.DepartmentId.Value.ToString(), e.PositionId.Value.ToString(),
        e.ShiftType, e.ScheduleId!.Value.ToString(), null, e.OvertimeAuthorized, e.OvertimeCalculationMethod, e.OvertimeCapType,
        e.OvertimeCapMinutes, e.CalculateOvertimeBeforeEntry, e.CardNumber, e.DevicePassword, e.Photo, e.DevicePrivilege,
        e.GetEffectiveRestDays().Select(d => (int)d).ToList());

    [Fact]
    public void User_EditsPersonalDataButNotScheduleOvertimeOrClockPrivilege()
    {
        var user = new FakePermissions(Permissions.EmployeesEdit);
        var employee = NewEmployee();

        EmployeeFieldPermissions.ForUpdate(user, employee, SameAs(employee) with { FirstName = "Otro", PhoneNumber = "555" }).Should().BeNull();
        EmployeeFieldPermissions.ForUpdate(user, employee, SameAs(employee) with { RestDays = new List<int> { 6 } }).Should().NotBeNull();
        EmployeeFieldPermissions.ForUpdate(user, employee, SameAs(employee) with { OvertimeCalculationMethod = OvertimeCalculationMethod.RoundByHour }).Should().NotBeNull();
        EmployeeFieldPermissions.ForUpdate(user, employee, SameAs(employee) with { DevicePrivilege = DevicePrivilege.Admin }).Should().NotBeNull();

        var hr = new FakePermissions(Permissions.EmployeesEdit, Permissions.EmployeesEditSchedule, Permissions.EmployeesEditOvertime);
        EmployeeFieldPermissions.ForUpdate(hr, employee, SameAs(employee) with { RestDays = new List<int> { 6 }, OvertimeAuthorized = false }).Should().BeNull();
        EmployeeFieldPermissions.ForUpdate(hr, employee, SameAs(employee) with { DevicePrivilege = DevicePrivilege.Admin }).Should().NotBeNull();
    }

    [Fact]
    public void NewEmployees_EnterAsNormalClockUsers_AndWithoutOvertimeUnlessHrAuthorizes()
    {
        var user = new FakePermissions(Permissions.EmployeesCreate);
        EmployeeFieldPermissions.ForCreate(user, false, DevicePrivilege.User).Should().BeNull();
        EmployeeFieldPermissions.ForCreate(user, true, DevicePrivilege.User).Should().NotBeNull();
        EmployeeFieldPermissions.ForCreate(user, false, DevicePrivilege.Admin).Should().NotBeNull();

        var hr = new FakePermissions(Permissions.EmployeesCreate, Permissions.EmployeesEditOvertime);
        EmployeeFieldPermissions.ForCreate(hr, true, DevicePrivilege.User).Should().BeNull();
    }

    // ---------- Marcajes manuales, motivos, borrado, roster e historial ----------

    private static AttendanceRecalculationHarness Week()
    {
        var shift = Shift.Create("Fijo 9-17", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = new AttendanceRecalculationHarness { Configuration = SystemConfiguration.CreateDefault() };
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Permisos", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Domingo }));
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddHours(9), CheckType.CheckIn));
        h.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, Monday.AddHours(17), CheckType.CheckIn));
        return h;
    }

    private static ICurrentUserService User(AttendanceRecalculationHarness h)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.UserName).Returns(() => h.CurrentUser);
        return user.Object;
    }

    private static Task<Result> CaptureAsync(AttendanceRecalculationHarness h, DateTime civil, string? reason) =>
        new RegisterManualAttendanceCommandHandler(h.AttendanceRepository, h.UnitOfWork, DefaultTimeNormalizer.Instance,
                currentUser: User(h), history: h.AdjustmentRepository)
            .Handle(new RegisterManualAttendanceCommand(Emp.Value, civil, AttendanceRegressionFixtures.TimeZoneMexicoCity, reason), CancellationToken.None);

    private static Task<Result> DeleteAsync(AttendanceRecalculationHarness h, AttendanceRecord record, string? reason) =>
        new DeleteManualAttendanceCommandHandler(h.AttendanceRepository, h.AdjustmentRepository, h.Consolidations, NoOpConsolidationWakeup.Instance,
                h.UnitOfWork, h.LockService, h.Sender, NullLogger<DeleteManualAttendanceCommandHandler>.Instance, User(h))
            .Handle(new DeleteManualAttendanceCommand(record.Id.Value.ToString(), reason), CancellationToken.None);

    private static DailyAttendance Day(AttendanceRecalculationHarness h) => h.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == Monday);

    [Fact]
    public async Task ManualPunch_RequiresAReason_AndRecordsWhoWhenAndWhy()
    {
        var h = Week();

        (await CaptureAsync(h, Monday.AddHours(18), "  ")).Error.Should().Be(AdjustmentReasons.RequiredMessage);
        h.Store.Records.Should().HaveCount(2);

        h.CurrentUser = "ana.rh";
        (await CaptureAsync(h, Monday.AddHours(18), "Olvidó checar la salida")).IsSuccess.Should().BeTrue();

        var manual = h.Store.Records.Single(r => r.IsManual);
        manual.RegisteredBy.Should().Be("ana.rh");
        manual.Reason.Should().Be("Olvidó checar la salida");
        manual.RegisteredAt.Should().NotBeNull();
        h.Store.AdjustmentHistory.Should().ContainSingle(e => e.Action == AdjustmentReasons.ManualPunch && e.Reason == "Olvidó checar la salida" && e.ChangedBy == "ana.rh");
    }

    [Theory]
    [InlineData("Salida")]
    [InlineData(ManuallyAssignAttendanceCommandHandler.Unassign)]
    public async Task AssigningOrUnassigningAPunch_RequiresAReason(string assignment)
    {
        var h = Week();
        await h.RecalculateAsync(Monday, Monday);
        var exit = h.Store.Records.Single(r => r.CheckTime.Hour == 23);

        var result = await h.ManualAssign().Handle(new ManuallyAssignAttendanceCommand(Emp.Value, DateOnly.FromDateTime(Monday), exit.Id.Value.ToString(), assignment), CancellationToken.None);

        result.Error.Should().Be(AdjustmentReasons.RequiredMessage);
        h.Store.Adjustments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletingAManualPunch_RemovesItsDecisions_RecalculatesAndLeavesItInTheHistory()
    {
        var h = Week();
        await CaptureAsync(h, Monday.AddHours(18), "Salida tarde");
        await h.RecalculateAsync(Monday, Monday);
        var manual = h.Store.Records.Single(r => r.IsManual);
        (await h.ManualAssign().Handle(new ManuallyAssignAttendanceCommand(Emp.Value, DateOnly.FromDateTime(Monday), manual.Id.Value.ToString(), "Salida", Reason: "Prueba"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        Day(h).CivilCheckOut!.Value.Hour.Should().Be(18);

        (await DeleteAsync(h, manual, null)).Error.Should().Be(AdjustmentReasons.RequiredMessage);
        (await DeleteAsync(h, manual, "Se capturó por error")).IsSuccess.Should().BeTrue();

        h.Store.Records.Should().NotContain(manual);
        h.Store.Adjustments.Single().DecidedRecordIds().Should().NotContain(manual.Id);
        Day(h).CivilCheckOut!.Value.Hour.Should().Be(17);
        h.Store.AdjustmentHistory.Should().Contain(e => e.Action == AdjustmentReasons.DeleteManualPunch && e.Reason == "Se capturó por error" && e.Before.Contains("Salida tarde"));
    }

    [Fact]
    public async Task OnlyManualPunchesCanBeDeleted()
    {
        var h = Week();
        var devicePunch = h.Store.Records.First();

        (await DeleteAsync(h, devicePunch, "Prueba")).Error.Should().Be("Sólo se pueden eliminar marcajes manuales.");
        h.Store.Records.Should().Contain(devicePunch);
    }

    [Fact]
    public async Task ReturningToTheRoster_RemovesTheManualShiftAndRestDay()
    {
        var h = Week();
        await h.RecalculateAsync(Monday, Monday);
        var other = Shift.Create("Vespertino", new TimeSpan(14, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);
        h.Store.Shifts.Add(other);
        (await h.Decisions().DecideAsync(Emp, Monday, "Turno", null, null, (c, a) =>
        {
            a.SetShiftOverride(other.Id, c.Author);
            a.SetRestDayOverride(true, c.Author);
            return Task.FromResult(Result.Success());
        }, CancellationToken.None)).IsSuccess.Should().BeTrue();
        Day(h).IsRestDay.Should().BeTrue();

        var shift = await new ClearDailyShiftOverrideCommandHandler(h.Decisions()).Handle(new ClearDailyShiftOverrideCommand(Emp.Value, DateOnly.FromDateTime(Monday)), CancellationToken.None);
        var rest = await new ClearDailyRestDayOverrideCommandHandler(h.Decisions()).Handle(new ClearDailyRestDayOverrideCommand(Emp.Value, DateOnly.FromDateTime(Monday)), CancellationToken.None);

        shift.IsSuccess.Should().BeTrue(shift.IsSuccess ? "" : shift.Error);
        rest.IsSuccess.Should().BeTrue(rest.IsSuccess ? "" : rest.Error);
        h.Store.Adjustments.Single().ShiftIdOverride.Should().BeNull();
        h.Store.Adjustments.Single().IsRestDayOverride.Should().BeNull();
        Day(h).IsRestDay.Should().BeFalse();
        Day(h).ShiftId.Should().NotBe(other.Id);

        (await new ClearDailyShiftOverrideCommandHandler(h.Decisions()).Handle(new ClearDailyShiftOverrideCommand(Emp.Value, DateOnly.FromDateTime(Monday)), CancellationToken.None))
            .Error.Should().Be("El día no tiene un cambio manual de turno.");
    }

    [Fact]
    public async Task ADecisionOnAnOldVersionOfTheDay_IsRejected()
    {
        var h = Week();
        await h.RecalculateAsync(Monday, Monday);
        await h.Decisions().DecideAsync(Emp, Monday, "Descanso", null, null, (c, a) => { a.SetRestDayOverride(true, c.Author); return Task.FromResult(Result.Success()); }, CancellationToken.None);
        var seen = h.Store.Adjustments.Single().Revision;
        // Otra persona cambia el día mientras éste se veía.
        await h.Decisions().DecideAsync(Emp, Monday, "Descanso", null, null, (c, a) => { a.SetRestDayOverride(false, c.Author); return Task.FromResult(Result.Success()); }, CancellationToken.None);

        var result = await new ClearDailyRestDayOverrideCommandHandler(h.Decisions())
            .Handle(new ClearDailyRestDayOverrideCommand(Emp.Value, DateOnly.FromDateTime(Monday), ExpectedRevision: seen), CancellationToken.None);

        result.Error.Should().Contain("mientras se editaba");
        h.Store.Adjustments.Single().IsRestDayOverride.Should().BeFalse();
    }

    [Fact]
    public async Task TheDayHistory_ShowsWhoWhenWhatAndWhy_NewestFirst()
    {
        var h = Week();
        await h.RecalculateAsync(Monday, Monday);
        h.CurrentUser = "ana.rh";
        await CaptureAsync(h, Monday.AddHours(18), "Olvidó checar");
        h.CurrentUser = "luis.admin";
        var exit = h.Store.Records.Single(r => r.CheckTime.Hour == 23);
        await h.ManualAssign().Handle(new ManuallyAssignAttendanceCommand(Emp.Value, DateOnly.FromDateTime(Monday), exit.Id.Value.ToString(), "None", Reason: "Salida duplicada"), CancellationToken.None);

        var history = await new GetAttendanceHistoryQueryHandler(h.AdjustmentRepository).Handle(new GetAttendanceHistoryQuery(Emp.Value, DateOnly.FromDateTime(Monday)), CancellationToken.None);

        history.Select(e => (e.ChangedBy, e.Action, e.Reason)).Should().Equal(
            ("luis.admin", "Desasignó un marcaje", "Salida duplicada"),
            ("ana.rh", "Agregó un marcaje manual", "Olvidó checar"));
    }
}
