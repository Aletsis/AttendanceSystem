using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;

/// <summary>
/// F02-02 (parte de H07) y F02-07: asignación manual con pertenencia y diagnóstico de asignaciones heredadas.
/// </summary>
public class Phase02ManualAssignmentAndDiagnosticTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly EmployeeId EmployeeA = EmployeeId.From("EMP-ROT-002");
    private static readonly EmployeeId EmployeeB = EmployeeId.From("EMP-FIX-001");

    private static Employee CreateEmployee(EmployeeId id) =>
        Employee.Create(id, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            ShiftType.Rotativo, null, restDays: Array.Empty<WeekDay>());

    private static AttendanceRecord AddPunch(AttendanceRecalculationHarness h, EmployeeId id, DateTime civil)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(id, civil, CheckType.CheckIn);
        h.Store.Records.Add(record);
        return record;
    }

    private static async Task<AttendanceRecalculationHarness> NightThenRegularAsync()
    {
        var h = new AttendanceRecalculationHarness();
        var night = AttendanceRegressionFixtures.CreateNightShift();
        var regular = AttendanceRegressionFixtures.CreateRegularShift();
        h.Store.Shifts.AddRange(new[] { night, regular });
        h.Store.Employees.Add(CreateEmployee(EmployeeA));
        h.Store.Rosters.Add(ShiftRoster.Create(EmployeeA, Monday, night.Id, isRestDay: false));
        h.Store.Rosters.Add(ShiftRoster.Create(EmployeeA, Tuesday, regular.Id, isRestDay: false));
        AddPunch(h, EmployeeA, Monday.AddHours(22));
        AddPunch(h, EmployeeA, Tuesday.AddHours(6));
        AddPunch(h, EmployeeA, Tuesday.AddHours(8));
        AddPunch(h, EmployeeA, Tuesday.AddHours(16));
        await h.RecalculateAsync(Monday, Tuesday);
        return h;
    }

    private static ManuallyAssignAttendanceCommandHandler CreateHandler(AttendanceRecalculationHarness h) =>
        new(h.DailyRepository, h.AttendanceRepository, h.AdjustmentRepository, h.AssignmentRepository, h.UnitOfWork, h.LockService);

    [Fact]
    public async Task ManualAssignment_RejectsPunchAlreadyUsedByAnotherJornada_WithoutWrites()
    {
        var h = await NightThenRegularAsync();
        var mondayExit = h.Punch(EmployeeA, Tuesday.AddHours(6));
        var savesBefore = h.Store.SaveChangesCount;
        var tuesdayCheckInBefore = h.Daily(EmployeeA, Tuesday)!.CheckInRecordId;

        var result = await CreateHandler(h).Handle(
            new ManuallyAssignAttendanceCommand(EmployeeA.Value, DateOnly.FromDateTime(Tuesday), mondayExit.Id.Value.ToString(), "Entrada"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("03/08/2026");
        h.Store.SaveChangesCount.Should().Be(savesBefore);
        h.Daily(EmployeeA, Tuesday)!.CheckInRecordId.Should().Be(tuesdayCheckInBefore);
        h.Store.Adjustments.Should().BeEmpty();
    }

    [Fact]
    public async Task ManualAssignment_WithinSameJornada_UpdatesRowsAndSurvivesRecalculation()
    {
        var h = await NightThenRegularAsync();
        // Sin salida automática: RH asigna como salida del martes el marcaje de las 16:00 tras desasignarlo.
        var exit16 = h.Punch(EmployeeA, Tuesday.AddHours(16));
        var handler = CreateHandler(h);

        (await handler.Handle(new ManuallyAssignAttendanceCommand(EmployeeA.Value, DateOnly.FromDateTime(Tuesday), exit16.Id.Value.ToString(), "None"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        h.Store.Assignments.Should().NotContain(a => a.AttendanceRecordId == exit16.Id);
        exit16.Status.Should().Be(AttendanceStatus.Pending);

        (await handler.Handle(new ManuallyAssignAttendanceCommand(EmployeeA.Value, DateOnly.FromDateTime(Tuesday), exit16.Id.Value.ToString(), "Salida"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        var row = h.Store.Assignments.Single(a => a.AttendanceRecordId == exit16.Id);
        row.Role.Should().Be(PunchAssignmentRole.CheckOut);
        row.Origin.Should().Be(PunchAssignmentOrigin.Manual);

        await h.RecalculateAsync(Tuesday, Tuesday);

        h.Daily(EmployeeA, Tuesday)!.CheckOutRecordId.Should().Be(exit16.Id);
        h.Store.Assignments.Single(a => a.AttendanceRecordId == exit16.Id).Origin.Should().Be(PunchAssignmentOrigin.Manual);
    }

    [Fact]
    public async Task Diagnostic_DetectsLegacyInconsistencies_AndRepairIsReviewableAndIdempotent()
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Employees.Add(CreateEmployee(EmployeeA));
        h.Store.Employees.Add(CreateEmployee(EmployeeB));
        var shift = AttendanceRegressionFixtures.CreateRegularShift();

        var reused = AddPunch(h, EmployeeA, Tuesday.AddHours(6));        // H02: salida del lunes y entrada del martes
        reused.MarkAsProcessed();
        var clean = AddPunch(h, EmployeeA, Tuesday.AddHours(16));        // referencia inequívoca
        clean.MarkAsProcessed();
        var pendingRef = AddPunch(h, EmployeeA, Monday.AddHours(22));    // referenciado pero Pending
        var distant = AddPunch(h, EmployeeB, Monday.AddDays(3).AddHours(8)); // jueves en la jornada del lunes
        distant.MarkAsProcessed();

        h.Store.Dailies.Add(DailyAttendance.Create(EmployeeA, Monday, shift, pendingRef.CheckTime, reused.CheckTime, false, pendingRef.Id, reused.Id));
        h.Store.Dailies.Add(DailyAttendance.Create(EmployeeA, Tuesday, shift, reused.CheckTime, clean.CheckTime, false, reused.Id, clean.Id));
        h.Store.Dailies.Add(DailyAttendance.Create(EmployeeB, Monday, shift, distant.CheckTime, null, false, distant.Id, null));

        var service = new PunchAssignmentDiagnosticService(h.DailyRepository, h.AttendanceRepository, h.AdjustmentRepository,
            h.AssignmentRepository, h.UnitOfWork, NullLogger<PunchAssignmentDiagnosticService>.Instance);

        var report = await service.DiagnoseAsync(DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Tuesday));

        report.Issues.Should().Contain(i => i.Kind == PunchAssignmentIssueKind.MultipleReferences && i.RecordId == reused.Id.Value.ToString());
        report.Issues.Should().Contain(i => i.Kind == PunchAssignmentIssueKind.IncompatibleStatus && i.RecordId == pendingRef.Id.Value.ToString());
        report.Issues.Should().Contain(i => i.Kind == PunchAssignmentIssueKind.TemporalIncompatibility && i.RecordId == distant.Id.Value.ToString(),
            "un marcaje del jueves queda fuera de la ventana [D-1, D+1] de la jornada del lunes");
        report.ProposedBackfillCount.Should().Be(1, "sólo la referencia inequívoca se materializa automáticamente");
        report.JornadasToRecalculate.Should().Contain(new JornadaKey(EmployeeA.Value, DateOnly.FromDateTime(Monday)))
            .And.Contain(new JornadaKey(EmployeeA.Value, DateOnly.FromDateTime(Tuesday)));

        var dryRun = await service.RepairAsync(DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Tuesday), dryRun: true);
        dryRun.CreatedCount.Should().Be(1);
        h.Store.Assignments.Should().BeEmpty("la simulación no escribe");

        var applied = await service.RepairAsync(DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Tuesday), dryRun: false);
        applied.CreatedCount.Should().Be(1);
        h.Store.Assignments.Should().ContainSingle(a => a.AttendanceRecordId == clean.Id && a.Role == PunchAssignmentRole.CheckOut);

        var second = await service.RepairAsync(DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Tuesday), dryRun: false);
        second.CreatedCount.Should().Be(0, "la reparación es idempotente");
        second.AlreadyMaterializedCount.Should().Be(1);
    }

    [Fact]
    public async Task Diagnostic_ReportsForeignEmployeeReference()
    {
        var h = new AttendanceRecalculationHarness();
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var foreign = AddPunch(h, EmployeeB, Monday.AddHours(8));
        foreign.MarkAsProcessed();
        h.Store.Dailies.Add(DailyAttendance.Create(EmployeeA, Monday, shift, foreign.CheckTime, null, false, foreign.Id, null));

        var service = new PunchAssignmentDiagnosticService(h.DailyRepository, h.AttendanceRepository, h.AdjustmentRepository,
            h.AssignmentRepository, h.UnitOfWork, NullLogger<PunchAssignmentDiagnosticService>.Instance);

        var report = await service.DiagnoseAsync(DateOnly.FromDateTime(Monday), DateOnly.FromDateTime(Monday));

        report.Issues.Should().ContainSingle(i => i.Kind == PunchAssignmentIssueKind.ForeignEmployee)
            .Which.EmployeeId.Should().Be(EmployeeA.Value);
        report.ProposedBackfillCount.Should().Be(0);
    }
}
