using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// F03-08: pruebas relacionales adversas sobre PostgreSQL real. Cada actor usa su propio DbContext y,
/// por tanto, su propia conexión y proceso servidor; las colisiones se fuerzan con barreras y ganchos,
/// y las esperas de bloqueo se observan en pg_locks (no con pausas arbitrarias).
/// </summary>
public class PostgresConcurrencyTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Zone = "America/Mexico_City";
    private static readonly TimeSpan UtcMinus6 = TimeSpan.FromHours(-6);
    private readonly PostgresTestDatabaseFixture _fixture;
    private readonly IPersistenceFailureClassifier _classifier = new NpgsqlPersistenceFailureClassifier();

    public PostgresConcurrencyTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    #region Ingesta idempotente (F03-01 / F03-05 / F03-06)

    [Fact]
    public async Task TwoConnections_InsertTheSamePunch_OneRow_SameIdForBoth_AndASingleOutboxEvent()
    {
        if (!_fixture.IsAvailable) return;

        var checkTimeUtc = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc); // 08:00 civil
        var barrier = new AsyncBarrier(2);

        async Task<Guid> RecordAsync(string deviceId)
        {
            await using var context = _fixture.CreateDbContext();
            // Ambos comprueban existencia antes de que cualquiera inserte: la colisión es inevitable.
            var repository = new HookedAttendanceRepository(new AttendanceRepository(context), onFirstIdentityLookup: barrier.SignalAndWaitAsync);
            var handler = new RecordAttendanceCommandHandler(repository, context, DefaultTimeNormalizer.Instance, null,
                NullLogger<RecordAttendanceCommandHandler>.Instance, _classifier);
            var result = await handler.Handle(new RecordAttendanceCommand("EMP-PG-C01", deviceId, checkTimeUtc, 1, 0, Zone, "Test"), CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);

            // El llamador sigue usando el mismo contexto (p. ej. el controlador ADMS actualiza el dispositivo):
            // nada de la inserción fallida debe colarse en ese guardado.
            await context.SaveChangesAsync();
            return result.Value;
        }

        // Mismo marcaje llegado por dos vías distintas (dispositivo y nube): la identidad no incluye el dispositivo.
        var results = await Task.WhenAll(RecordAsync("DEV-PG-A"), RecordAsync("DEV-PG-CLOUD"));

        results[0].Should().Be(results[1], "ambos procesos obtienen el identificador de la única fila");
        await using var verify = _fixture.CreateDbContext();
        (await verify.AttendanceRecords.CountAsync(r => r.EmployeeId == EmployeeId.From("EMP-PG-C01"))).Should().Be(1);
        (await CountRecordedEventsAsync(verify, results[0])).Should().Be(1, "el perdedor no deja eventos huérfanos en el outbox");
        (await CountEmployeeEventsAsync(verify, "EMP-PG-C01")).Should().Be(1, "ningún evento referencia un registro que no se guardó");
    }

    [Fact]
    public async Task Batch_WithOneConcurrentCollisionAndTenNewPunches_SavesTheTen_WithTheirEvents()
    {
        if (!_fixture.IsAvailable) return;

        var employee = "EMP-PG-C02";
        var baseUtc = new DateTime(2026, 6, 2, 14, 0, 0, DateTimeKind.Utc);
        var colliding = baseUtc;
        var logs = Enumerable.Range(0, 11).Select(i => new AttendanceLogDto(employee, baseUtc.AddMinutes(i * 30), 1, 0)).ToList();

        await using var context = _fixture.CreateDbContext();
        // Tras la comprobación de existencia, otro proceso inserta uno de los marcajes del lote.
        var repository = new HookedAttendanceRepository(new AttendanceRepository(context), onFirstIdentityLookup: async () =>
        {
            await using var concurrent = _fixture.CreateDbContext();
            concurrent.AttendanceRecords.Add(Punch(employee, "DEV-PG-OTHER", colliding));
            await concurrent.SaveChangesAsync();
        });
        var handler = new RecordAttendanceBatchCommandHandler(repository, context, DefaultTimeNormalizer.Instance, null,
            NullLogger<RecordAttendanceBatchCommandHandler>.Instance, _classifier);

        var result = await handler.Handle(new RecordAttendanceBatchCommand("DEV-PG-ADMS", logs, Zone), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        result.Value.Should().Be(10, "la colisión confirmada se omite y los diez nuevos se guardan");

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.AttendanceRecords.Where(r => r.EmployeeId == EmployeeId.From(employee)).ToListAsync();
        rows.Should().HaveCount(11);
        foreach (var row in rows)
        {
            (await CountRecordedEventsAsync(verify, row.Id.Value)).Should().Be(1, $"cada fila insertada conserva su evento ({row.CheckTime:HH:mm})");
        }
        (await CountEmployeeEventsAsync(verify, employee)).Should().Be(11, "un evento por fila: sin eventos del intento fallido");
    }

    [Fact]
    public async Task ConnectionFailure_IsReportedAsRecoverableFailure_NeverAsDuplicates()
    {
        // No requiere servidor: el puerto 1 rechaza la conexión.
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2")
            .Options;
        await using var context = new AttendanceDbContext(options, Mock.Of<IPublisher>());
        var handler = new RecordAttendanceBatchCommandHandler(new AttendanceRepository(context), context, DefaultTimeNormalizer.Instance, null,
            NullLogger<RecordAttendanceBatchCommandHandler>.Instance, _classifier);

        var result = await handler.Handle(new RecordAttendanceBatchCommand("DEV-PG-OFF",
            new List<AttendanceLogDto> { new("EMP-PG-C03", new DateTime(2026, 6, 3, 14, 0, 0, DateTimeKind.Utc), 1, 0) }, Zone),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(nameof(PersistenceFailureKind.Connection)).And.Contain("reintentable");
    }

    #endregion

    #region Exclusión entre procesos (F03-02 / F03-07)

    [Fact]
    public async Task ConcurrentRecalculation_AndManualAssignment_Serialize_AndTheManualDecisionSurvives()
    {
        if (!_fixture.IsAvailable) return;

        var employee = CreateEmployee("EMP-PG-C04");
        var day = new DateTime(2026, 6, 8);
        var regular = Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var checkIn = Punch(employee.Id.Value, "DEV-PG-04", CivilToUtc(day.AddHours(8)), day.AddHours(8));
        var realExit = Punch(employee.Id.Value, "DEV-PG-04", CivilToUtc(day.AddHours(16)), day.AddHours(16));
        var lateExtra = Punch(employee.Id.Value, "DEV-PG-04", CivilToUtc(day.AddHours(18).AddMinutes(30)), day.AddHours(18).AddMinutes(30));
        await SeedAsync(checkIn, realExit, lateExtra);
        await RecalculateAsync(employee, regular, day);

        // A: recálculo que se detiene con el bloqueo tomado.
        var acquired = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var recalculation = Task.Run(() => RecalculateAsync(employee, regular, day, inner => new PausingLockService(inner, acquired, release.Task)));
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // B: RH asigna la salida real mientras A sigue en curso; debe esperar al bloqueo del empleado.
        var manual = Task.Run(async () =>
        {
            await using var context = _fixture.CreateDbContext();
            var handler = CreateManualAssign(context);
            return await handler.Handle(new ManuallyAssignAttendanceCommand(employee.Id.Value, DateOnly.FromDateTime(day), realExit.Id.Value.ToString(), "Salida"), CancellationToken.None);
        });

        await WaitForAdvisoryWaitersAsync(1);
        manual.IsCompleted.Should().BeFalse("la asignación manual espera a que termine el recálculo");

        release.SetResult();
        await recalculation;
        var manualResult = await manual;
        manualResult.IsSuccess.Should().BeTrue(manualResult.IsSuccess ? string.Empty : manualResult.Error);

        // Un recálculo posterior conserva la decisión manual.
        await RecalculateAsync(employee, regular, day);

        await using var verify = _fixture.CreateDbContext();
        var daily = await verify.DailyAttendances.SingleAsync(d => d.EmployeeId == employee.Id && d.Date == day);
        daily.CheckOutRecordId.Should().Be(realExit.Id);
        var row = await verify.PunchAssignments.SingleAsync(a => a.Id == realExit.Id);
        row.Origin.Should().Be(PunchAssignmentOrigin.Manual);
        (await verify.AttendanceAdjustments.SingleAsync(a => a.EmployeeId == employee.Id && a.Date == day)).CheckOutRecordId.Should().Be(realExit.Id);
        (await verify.AttendanceRecords.SingleAsync(r => r.Id == lateExtra.Id)).Status.Should().Be(AttendanceStatus.Pending);
    }

    [Fact]
    public async Task FailureInOneUnit_RestoresOnlyThatUnit_AndReportsPartialFailure()
    {
        if (!_fixture.IsAvailable) return;

        var healthy = CreateEmployee("EMP-PG-C05A");
        var broken = CreateEmployee("EMP-PG-C05B");
        var day = new DateTime(2026, 6, 9);
        var regular = Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);

        var healthyIn = Punch(healthy.Id.Value, "DEV-PG-05", CivilToUtc(day.AddHours(8)), day.AddHours(8));
        var brokenIn = Punch(broken.Id.Value, "DEV-PG-05", CivilToUtc(day.AddHours(8).AddMinutes(1)), day.AddHours(8).AddMinutes(1));
        await SeedAsync(healthyIn, brokenIn);
        await RecalculateAsync(broken, regular, day);

        Guid brokenDailyBefore;
        await using (var context = _fixture.CreateDbContext())
        {
            brokenDailyBefore = (await context.DailyAttendances.SingleAsync(d => d.EmployeeId == broken.Id)).Id.Value;
            // Ajuste heredado inválido: asigna al empleado roto un marcaje del sano.
            var adjustment = AttendanceAdjustment.Create(broken.Id, day, "Legado");
            adjustment.SetCheckIn(healthyIn.Id, healthyIn.CheckTime, "Legado");
            context.AttendanceAdjustments.Add(adjustment);
            await context.SaveChangesAsync();
        }

        var act = () => RecalculateAsync(new[] { healthy, broken }, regular, day);

        var failure = (await act.Should().ThrowAsync<AttendanceRecalculationException>()).Which;
        failure.TotalUnits.Should().Be(2);
        failure.SuccessfulUnits.Should().Be(1);
        failure.FailedUnits.Should().ContainSingle(f => f.EmployeeId == broken.Id.Value);

        await using var verify = _fixture.CreateDbContext();
        (await verify.DailyAttendances.SingleAsync(d => d.EmployeeId == healthy.Id)).CheckInRecordId.Should().Be(healthyIn.Id, "la unidad sana se confirmó");
        (await verify.DailyAttendances.SingleAsync(d => d.EmployeeId == broken.Id)).Id.Value.Should().Be(brokenDailyBefore, "la unidad fallida conserva su resultado anterior");
    }

    [Fact]
    public async Task CancellationWhileWaitingForLock_RollsBack_AndLeavesNoLock()
    {
        if (!_fixture.IsAvailable) return;

        var employee = EmployeeId.From("EMP-PG-C06");
        await using var holder = _fixture.CreateDbContext();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await CreateLock(holder).AcquireAsync(new[] { employee });

        using var cts = new CancellationTokenSource();
        var waiter = Task.Run(async () =>
        {
            await using var context = _fixture.CreateDbContext();
            await using var tx = await context.Database.BeginTransactionAsync();
            await CreateLock(context).AcquireAsync(new[] { employee }, cts.Token);
        });

        await WaitForAdvisoryWaitersAsync(1);
        cts.Cancel();
        var cancelled = () => waiter;
        var thrown = (await cancelled.Should().ThrowAsync<Exception>()).Which;
        _classifier.Classify(thrown).Kind.Should().BeOneOf(PersistenceFailureKind.Cancelled, PersistenceFailureKind.Timeout);

        await holderTx.CommitAsync();

        // Ni el titular confirmado ni el cancelado dejan bloqueos: un tercero lo obtiene de inmediato.
        await AssertCanAcquireQuicklyAsync(employee);
    }

    [Fact]
    public async Task ProcessDeath_ReleasesTheLock()
    {
        if (!_fixture.IsAvailable) return;

        var employee = EmployeeId.From("EMP-PG-C07");
        var holder = _fixture.CreateDbContext();
        await holder.Database.BeginTransactionAsync();
        await CreateLock(holder).AcquireAsync(new[] { employee });
        var pid = await holder.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();

        // Simula la muerte del proceso cliente: el servidor termina su sesión sin commit ni rollback.
        await using (var admin = _fixture.CreateDbContext())
        {
            await admin.Database.ExecuteSqlRawAsync("SELECT pg_terminate_backend({0})", pid);
        }

        await AssertCanAcquireQuicklyAsync(employee);
        try { await holder.DisposeAsync(); } catch { /* conexión ya terminada */ }
    }

    [Fact]
    public async Task LockWait_BeyondTimeout_IsClassifiedAsLockTimeout()
    {
        if (!_fixture.IsAvailable) return;

        var employee = EmployeeId.From("EMP-PG-C08");
        await using var holder = _fixture.CreateDbContext();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await CreateLock(holder).AcquireAsync(new[] { employee });

        await using var context = _fixture.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var act = () => CreateLock(context, TimeSpan.FromMilliseconds(200)).AcquireAsync(new[] { employee });

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        _classifier.Classify(thrown).Kind.Should().Be(PersistenceFailureKind.LockTimeout);
    }

    [Fact]
    public async Task OppositeRequestOrder_AcquiresInTheSameKeyOrder_WithoutDeadlock()
    {
        if (!_fixture.IsAvailable) return;

        var a = EmployeeId.From("EMP-PG-C09A");
        var b = EmployeeId.From("EMP-PG-C09B");
        var barrier = new AsyncBarrier(2);

        async Task RunAsync(EmployeeId[] order)
        {
            await using var context = _fixture.CreateDbContext();
            await using var tx = await context.Database.BeginTransactionAsync();
            await barrier.SignalAndWaitAsync();
            await CreateLock(context, TimeSpan.FromSeconds(10)).AcquireAsync(order);
            await tx.CommitAsync();
        }

        var act = () => Task.WhenAll(RunAsync(new[] { a, b }), RunAsync(new[] { b, a }));
        await act.Should().NotThrowAsync("el orden por clave evita el interbloqueo 40P01");
    }

    #endregion

    #region Decisiones manuales (bloqueo + revisión + auditoría)

    [Fact]
    public async Task TwoOperatorsOnTheSameRevision_OneWins_TheOtherGetsAVisibleConflict()
    {
        if (!_fixture.IsAvailable) return;

        var employee = CreateEmployee("EMP-PG-D01");
        var day = new DateTime(2026, 6, 15);
        var regular = Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var early = Punch(employee.Id.Value, "DEV-PG-D", CivilToUtc(day.AddHours(7).AddMinutes(55)), day.AddHours(7).AddMinutes(55));
        var late = Punch(employee.Id.Value, "DEV-PG-D", CivilToUtc(day.AddHours(8).AddMinutes(20)), day.AddHours(8).AddMinutes(20));
        var exit = Punch(employee.Id.Value, "DEV-PG-D", CivilToUtc(day.AddHours(16)), day.AddHours(16));
        await SeedAsync(early, late, exit);
        await RecalculateAsync(employee, regular, day);

        var barrier = new AsyncBarrier(2);
        async Task<AttendanceSystem.Application.Common.Result> DecideAsync(AttendanceRecord record)
        {
            await using var context = _fixture.CreateDbContext();
            var handler = CreateManualAssign(context);
            await barrier.SignalAndWaitAsync();
            return await handler.Handle(new ManuallyAssignAttendanceCommand(employee.Id.Value, DateOnly.FromDateTime(day), record.Id.Value.ToString(), "Entrada", ExpectedRevision: 0), CancellationToken.None);
        }

        var results = await Task.WhenAll(DecideAsync(early), DecideAsync(late));

        results.Count(r => r.IsSuccess).Should().Be(1, "el bloqueo serializa y la revisión esperada detecta la decisión obsoleta");
        results.Single(r => r.IsFailure).Error.Should().Contain("revisión 1");

        await using var verify = _fixture.CreateDbContext();
        var adjustment = await verify.AttendanceAdjustments.SingleAsync(a => a.EmployeeId == employee.Id && a.Date == day);
        adjustment.Revision.Should().Be(1);
        adjustment.CheckInOverride.Should().Be(PositionOverrideKind.Assigned);
        var history = await verify.AttendanceAdjustmentHistory.Where(h => h.EmployeeId == employee.Id).ToListAsync();
        history.Should().ContainSingle();
        history[0].Revision.Should().Be(1);
        history[0].After.Should().Contain($"CheckIn=asignado:{adjustment.CheckInRecordId}");
        (await verify.AttendanceConsolidationRequests.CountAsync(r => r.EmployeeId == employee.Id)).Should().Be(1, "la decisión deja una consolidación durable");
    }

    [Fact]
    public async Task ForcedEmpty_IsPersisted_AndSurvivesRecalculations()
    {
        if (!_fixture.IsAvailable) return;

        var employee = CreateEmployee("EMP-PG-D02");
        var day = new DateTime(2026, 6, 16);
        var regular = Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);
        var checkIn = Punch(employee.Id.Value, "DEV-PG-D", CivilToUtc(day.AddHours(8)), day.AddHours(8));
        var exit = Punch(employee.Id.Value, "DEV-PG-D", CivilToUtc(day.AddHours(16)), day.AddHours(16));
        await SeedAsync(checkIn, exit);
        await RecalculateAsync(employee, regular, day);

        await using (var context = _fixture.CreateDbContext())
        {
            var result = await CreateManualAssign(context).Handle(
                new ManuallyAssignAttendanceCommand(employee.Id.Value, DateOnly.FromDateTime(day), exit.Id.Value.ToString(), ManuallyAssignAttendanceCommandHandler.Unassign),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        }

        await RecalculateAsync(employee, regular, day);
        await RecalculateAsync(employee, regular, day);

        await using var verify = _fixture.CreateDbContext();
        (await verify.DailyAttendances.SingleAsync(d => d.EmployeeId == employee.Id && d.Date == day)).CheckOutRecordId.Should().BeNull();
        var row = await verify.PunchAssignments.SingleAsync(a => a.Id == exit.Id);
        row.Role.Should().Be(PunchAssignmentRole.ManuallyExcluded);
        row.Origin.Should().Be(PunchAssignmentOrigin.Manual);
        (await verify.AttendanceAdjustments.SingleAsync(a => a.EmployeeId == employee.Id)).CheckOutOverride.Should().Be(PositionOverrideKind.ForcedEmpty);
    }

    #endregion

    #region Vigencias laborales e incidencias de cálculo

    [Fact]
    public async Task EmploymentPeriodsAndScheduleAssignments_RoundTrip_AndCalculationIssueIsStored()
    {
        if (!_fixture.IsAvailable) return;

        var branch = AttendanceSystem.Domain.Aggregates.BranchAggregate.Branch.Create("H01", "Sucursal Historia", null);
        var department = AttendanceSystem.Domain.Aggregates.DepartmentAggregate.Department.Create("Depto Historia", null);
        var position = AttendanceSystem.Domain.Aggregates.PositionAggregate.Position.Create("Puesto Historia", null, 1000m);
        var morning = Shift.Create("Matutino historia", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var afternoon = Shift.Create("Vespertino historia", new TimeSpan(14, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);
        var employee = Employee.Create(EmployeeId.From("EMP-PG-H01"), "Empleado", "Historia", null, null, new DateTime(2024, 1, 1), Gender.Male,
            branch.Id, department.Id, position.Id, ShiftType.Matutino, morning.Id, restDays: Array.Empty<WeekDay>());
        employee.RecordAssignment(new DateTime(2026, 6, 10), afternoon.Id, ShiftType.Vespertino, branch.Id);
        employee.Terminate(new DateTime(2026, 6, 20));
        employee.Rehire(new DateTime(2026, 7, 1));

        var daily = DailyAttendance.Create(employee.Id, new DateTime(2026, 6, 15), afternoon, null, null, false);
        daily.MarkOpenInterval("Intervalo abierto sin salida registrada desde las 13:00.");

        await using (var context = _fixture.CreateDbContext())
        {
            context.Branches.Add(branch);
            context.Departments.Add(department);
            context.Positions.Add(position);
            context.Shifts.AddRange(morning, afternoon);
            context.Employees.Add(employee);
            context.DailyAttendances.Add(daily);
            await context.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        var loaded = await verify.Employees.SingleAsync(e => e.Id == employee.Id);
        loaded.EmploymentPeriods.Should().HaveCount(2);
        loaded.IsActiveOn(new DateTime(2026, 6, 25)).Should().BeFalse("la interrupción entre períodos persiste");
        loaded.IsActiveOn(new DateTime(2026, 7, 2)).Should().BeTrue();
        loaded.AssignmentOn(new DateTime(2026, 6, 9)).ScheduleId.Should().Be(morning.Id);
        loaded.AssignmentOn(new DateTime(2026, 6, 10)).ScheduleId.Should().Be(afternoon.Id);

        var storedDaily = await verify.DailyAttendances.SingleAsync(d => d.Id == daily.Id);
        storedDaily.HasOpenInterval.Should().BeTrue();
        storedDaily.CalculationIssue.Should().Contain("13:00");
    }

    [Fact]
    public async Task LunchSettings_ShiftPaidMinutes_AndDailyLunchResult_RoundTrip()
    {
        if (!_fixture.IsAvailable) return;

        var shift = Shift.Create("Matutino comida", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        shift.SetPaidLunchMinutes(45);
        var daily = DailyAttendance.Create(EmployeeId.From("EMP-PG-L01"), new DateTime(2026, 6, 15), shift, null, null, false);
        daily.ApplyIntermediateAnalysis(lunchMinutesTaken: 55, lunchExcessMinutes: 10, hasTemporaryExits: false, temporaryExitMinutes: 0);
        var adjustment = AttendanceAdjustment.Create(EmployeeId.From("EMP-PG-L01"), new DateTime(2026, 6, 15), "rh");
        adjustment.SetLunchExcessForgiven(true, "rh");
        var excluded = AttendanceRecordId.CreateNew();
        adjustment.ExcludePunch(excluded, "rh");

        await using (var context = _fixture.CreateDbContext())
        {
            var config = await context.SystemConfigurations.FirstOrDefaultAsync();
            if (config == null)
            {
                config = SystemConfiguration.CreateDefault();
                context.SystemConfigurations.Add(config);
            }
            config.UpdateLunchSettings(new TimeSpan(13, 0, 0), new TimeSpan(16, 0, 0), 40);
            config.UpdateNightLunchWindow(new TimeSpan(23, 30, 0), new TimeSpan(3, 0, 0));
            context.Shifts.Add(shift);
            context.DailyAttendances.Add(daily);
            context.AttendanceAdjustments.Add(adjustment);
            await context.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateDbContext();
        var storedConfig = await verify.SystemConfigurations.FirstAsync();
        storedConfig.LunchPolicy.Should().Be(new LunchPolicy(new TimeSpan(13, 0, 0), new TimeSpan(16, 0, 0), 40));
        storedConfig.NightLunchPolicy.Should().Be(new LunchPolicy(new TimeSpan(23, 30, 0), new TimeSpan(3, 0, 0), 40));
        (await verify.Shifts.SingleAsync(s => s.Id == shift.Id)).PaidLunchMinutes.Should().Be(45);
        var storedDaily = await verify.DailyAttendances.SingleAsync(d => d.Id == daily.Id);
        storedDaily.LunchMinutesTaken.Should().Be(55);
        storedDaily.LunchExcessMinutes.Should().Be(10);
        storedDaily.LunchBreakMinutesApplied.Should().Be(10);
        var storedAdjustment = await verify.AttendanceAdjustments.SingleAsync(a => a.Id == adjustment.Id);
        storedAdjustment.LunchExcessForgiven.Should().BeTrue();
        storedAdjustment.ExcludedRecordIds().Should().ContainSingle().Which.Should().Be(excluded);
    }

    #endregion

    #region Infraestructura de prueba

    private static DateTime CivilToUtc(DateTime civil) => DateTime.SpecifyKind(civil - UtcMinus6, DateTimeKind.Utc);

    private static AttendanceRecord Punch(string employeeId, string deviceId, DateTime utc, DateTime? civil = null) =>
        AttendanceRecord.Create(EmployeeId.From(employeeId), DeviceId.From(deviceId), utc, VerifyMethod.Fingerprint, CheckType.CheckIn,
            workDate: DateOnly.FromDateTime(civil ?? utc + UtcMinus6), originalCheckTime: civil ?? utc + UtcMinus6, timeZoneId: Zone, source: "Test");

    private static Employee CreateEmployee(string id) =>
        Employee.Create(EmployeeId.From(id), "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Matutino, null, restDays: Array.Empty<WeekDay>());

    private async Task SeedAsync(params AttendanceRecord[] records)
    {
        await using var context = _fixture.CreateDbContext();
        context.AttendanceRecords.AddRange(records);
        await context.SaveChangesAsync();
    }

    private static async Task<int> CountRecordedEventsAsync(AttendanceDbContext context, Guid recordId)
    {
        var id = recordId.ToString();
        return await context.OutboxMessages.CountAsync(m => m.Type.Contains("AttendanceRecordedEvent") && m.Content.Contains(id));
    }

    private static async Task<int> CountEmployeeEventsAsync(AttendanceDbContext context, string employeeId) =>
        await context.OutboxMessages.CountAsync(m => m.Type.Contains("AttendanceRecordedEvent") && m.Content.Contains("\"" + employeeId + "\""));

    private PostgresAttendanceCalculationLockService CreateLock(AttendanceDbContext context, TimeSpan? timeout = null) =>
        new(context, NullLogger<PostgresAttendanceCalculationLockService>.Instance, timeout ?? TimeSpan.FromSeconds(30));

    /// <summary>Asignación manual con el servicio de decisiones real; el recálculo inmediato se omite (lo hace la prueba).</summary>
    private ManuallyAssignAttendanceCommandHandler CreateManualAssign(AttendanceDbContext context) =>
        new(new DailyAttendanceRepository(context), new AttendanceRepository(context), new PunchAssignmentRepository(context),
            new AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionService(
                new AttendanceAdjustmentRepository(context), new AttendanceConsolidationRepository(context), NoOpConsolidationWakeup.Instance,
                context, CreateLock(context), Mock.Of<ISender>(),
                NullLogger<AttendanceSystem.Application.Features.Attendance.Decisions.AttendanceDecisionService>.Instance,
                classifier: _classifier));

    private Task RecalculateAsync(Employee employee, Shift shift, DateTime day, Func<IAttendanceCalculationLockService, IAttendanceCalculationLockService>? decorate = null) =>
        RecalculateAsync(new[] { employee }, shift, day, decorate);

    private async Task RecalculateAsync(IReadOnlyList<Employee> employees, Shift shift, DateTime day, Func<IAttendanceCalculationLockService, IAttendanceCalculationLockService>? decorate = null)
    {
        await using var context = _fixture.CreateDbContext();
        var dailyRepo = new DailyAttendanceRepository(context);
        var attendanceRepo = new AttendanceRepository(context);

        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(employees.ToList());
        var assigned = employees.Select(e => ShiftRoster.Create(e.Id, day, shift.Id, isRestDay: false)).ToList();
        var rosterRepo = new Mock<IShiftRosterRepository>();
        rosterRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime s, DateTime e, BranchId? _, EmployeeId? emp, CancellationToken _) =>
                assigned.Where(r => r.Date >= s.Date && r.Date <= e.Date && (emp == null || r.EmployeeId == emp)).ToList());
        var shiftRepo = new Mock<IShiftRepository>();
        shiftRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { shift });
        var configRepo = new Mock<ISystemConfigurationRepository>();
        configRepo.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync((SystemConfiguration?)null);

        IAttendanceCalculationLockService lockService = CreateLock(context);
        if (decorate != null) lockService = decorate(lockService);

        var handler = new ProcessDailyAttendanceCommandHandler(
            dailyRepo, attendanceRepo, employeeRepo.Object, shiftRepo.Object, rosterRepo.Object, configRepo.Object,
            new AttendanceAdjustmentRepository(context), new PunchAssignmentRepository(context),
            lockService, context, new RegularSender(dailyRepo, attendanceRepo), NullLogger<ProcessDailyAttendanceCommandHandler>.Instance);

        await handler.Handle(new ProcessDailyAttendanceCommand(day, day, EmployeeId: employees.Count == 1 ? employees[0].Id : null), CancellationToken.None);
    }

    /// <summary>Espera, consultando pg_locks desde otra conexión, a que haya bloqueos asesores en espera.</summary>
    private async Task WaitForAdvisoryWaitersAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        await using var observer = _fixture.CreateDbContext();
        while (true)
        {
            var waiting = await observer.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())")
                .SingleAsync();
            if (waiting >= expected) return;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("No apareció la espera de bloqueo esperada en pg_locks.");
            await Task.Delay(25);
        }
    }

    private async Task AssertCanAcquireQuicklyAsync(EmployeeId employee)
    {
        await using var context = _fixture.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var act = () => CreateLock(context, TimeSpan.FromSeconds(2)).AcquireAsync(new[] { employee });
        await act.Should().NotThrowAsync("no deben quedar bloqueos permanentes");
        await tx.RollbackAsync();
    }

    private sealed class AsyncBarrier
    {
        private readonly int _participants;
        private int _arrived;
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AsyncBarrier(int participants) => _participants = participants;

        public Task SignalAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == _participants) _all.SetResult();
            return _all.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    /// <summary>Bloqueo real que, una vez adquirido, avisa y espera una señal antes de continuar.</summary>
    private sealed class PausingLockService : IAttendanceCalculationLockService
    {
        private readonly IAttendanceCalculationLockService _inner;
        private readonly TaskCompletionSource _acquired;
        private readonly Task _release;

        public PausingLockService(IAttendanceCalculationLockService inner, TaskCompletionSource acquired, Task release)
        {
            _inner = inner;
            _acquired = acquired;
            _release = release;
        }

        public async Task AcquireAsync(IEnumerable<EmployeeId> employeeIds, CancellationToken cancellationToken = default)
        {
            await _inner.AcquireAsync(employeeIds, cancellationToken);
            _acquired.TrySetResult();
            await _release.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    /// <summary>Repositorio real con un gancho tras la primera comprobación de identidad.</summary>
    private sealed class HookedAttendanceRepository : IAttendanceRepository
    {
        private readonly IAttendanceRepository _inner;
        private Func<Task>? _onFirstIdentityLookup;

        public HookedAttendanceRepository(IAttendanceRepository inner, Func<Task> onFirstIdentityLookup)
        {
            _inner = inner;
            _onFirstIdentityLookup = onFirstIdentityLookup;
        }

        public async Task<IReadOnlyList<AttendanceRecord>> GetByIdentitiesAsync(IReadOnlyCollection<(EmployeeId EmployeeId, DateTime CheckTimeUtc)> identities, CancellationToken cancellationToken = default)
        {
            var result = await _inner.GetByIdentitiesAsync(identities, cancellationToken);
            var hook = Interlocked.Exchange(ref _onFirstIdentityLookup, null);
            if (hook != null) await hook();
            return result;
        }

        public void Detach(AttendanceRecord record) => _inner.Detach(record);
        public Task<AttendanceRecord?> GetByIdAsync(AttendanceRecordId id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
        public Task<IReadOnlyList<AttendanceRecord>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDate, EmployeeId? employeeId = null, CancellationToken cancellationToken = default) => _inner.GetByDateRangeAsync(startDate, endDate, employeeId, cancellationToken);
        public Task<IReadOnlyList<AttendanceRecord>> GetByDeviceAndDateRangeAsync(DeviceId deviceId, DateTime startDateTime, DateTime endDateTime, CancellationToken cancellationToken = default) => _inner.GetByDeviceAndDateRangeAsync(deviceId, startDateTime, endDateTime, cancellationToken);
        public Task<IReadOnlyList<AttendanceRecord>> GetByDownloadLogIdAsync(DownloadLogId downloadLogId, CancellationToken cancellationToken = default) => _inner.GetByDownloadLogIdAsync(downloadLogId, cancellationToken);
        public Task<AttendanceRecord?> GetByEmployeeAndCheckTimeAsync(EmployeeId employeeId, DateTime checkTime, CancellationToken cancellationToken = default) => _inner.GetByEmployeeAndCheckTimeAsync(employeeId, checkTime, cancellationToken);
        public Task AddAsync(AttendanceRecord record, CancellationToken cancellationToken = default) => _inner.AddAsync(record, cancellationToken);
        public Task AddRangeAsync(IEnumerable<AttendanceRecord> records, CancellationToken cancellationToken = default) => _inner.AddRangeAsync(records, cancellationToken);
        public Task UpdateAsync(AttendanceRecord record, CancellationToken cancellationToken = default) => _inner.UpdateAsync(record, cancellationToken);
        public Task<bool> HasCheckInForDateAsync(EmployeeId employeeId, DateTime date, CancellationToken cancellationToken = default) => _inner.HasCheckInForDateAsync(employeeId, date, cancellationToken);
        public Task<IReadOnlyList<AttendanceRecord>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
    }

    private sealed class RegularSender : ISender
    {
        private readonly IDailyAttendanceRepository _dailyRepo;
        private readonly IAttendanceRepository _attendanceRepo;

        public RegularSender(IDailyAttendanceRepository dailyRepo, IAttendanceRepository attendanceRepo)
        {
            _dailyRepo = dailyRepo;
            _attendanceRepo = attendanceRepo;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            request is ProcessRegularAttendanceCommand c
                ? new ProcessRegularAttendanceCommandHandler(_dailyRepo, _attendanceRepo, NullLogger<ProcessRegularAttendanceCommandHandler>.Instance).Handle(c, cancellationToken)
                : throw new NotSupportedException(request.GetType().Name);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    #endregion
}
