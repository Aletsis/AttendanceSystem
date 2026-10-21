using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Queries;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// F07-03 / F07-04 / F07-07 sobre PostgreSQL real: el lector por lotes devuelve lo mismo que los repositorios, el
/// recálculo por lotes produce el mismo resultado que empleado por empleado, y cancelar conserva los lotes
/// confirmados y libera bloqueos.
/// </summary>
public class PostgresBatchRecalculationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Zone = "America/Mexico_City";
    private static readonly DateTime Monday = new(2026, 7, 6);
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresBatchRecalculationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    private sealed record Seeded(List<Employee> Employees, Shift Shift, BranchId Branch, BranchId OtherBranch);

    private async Task<Seeded> SeedAsync(string prefix, int employees, int days)
    {
        var branch = Branch.Create(prefix[..1] + "01", "Sucursal lote " + prefix, null, timeZoneId: Zone);
        var other = Branch.Create(prefix[..1] + "02", "Otra sucursal " + prefix, null, timeZoneId: Zone);
        var department = Department.Create("Depto lote " + prefix, null);
        var position = Position.Create("Puesto lote " + prefix, null, 1000m);
        department.AddPosition(position);
        var shift = Shift.Create("Matutino lote " + prefix, new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var list = new List<Employee>();
        var punches = new List<AttendanceRecord>();
        var adjustments = new List<AttendanceAdjustment>();
        for (int i = 0; i < employees; i++)
        {
            var employee = Employee.Create(EmployeeId.From($"{prefix}{i:D3}"), "Empleado", $"Lote {i}", null, null, new DateTime(2024, 1, 1), Gender.Male,
                i % 3 == 0 ? other.Id : branch.Id, department.Id, position.Id, ShiftType.Matutino, shift.Id, restDays: Array.Empty<WeekDay>(), overtimeAuthorized: true);
            if (i == 1) employee.RecordAssignment(Monday.AddDays(1), shift.Id, ShiftType.Matutino, other.Id); // cambia de sucursal a mitad
            if (i == 2) { employee.Terminate(Monday.AddDays(-30)); }                                       // de baja: fuera del rango
            list.Add(employee);
            for (int d = 0; d < days; d++)
            {
                var day = Monday.AddDays(d);
                punches.Add(Punch(employee.Id, day.AddHours(8).AddMinutes(i % 7)));
                punches.Add(Punch(employee.Id, day.AddHours(16).AddMinutes(i % 11 + 5)));
                if ((i + d) % 4 == 0)
                {
                    var adjustment = AttendanceAdjustment.Create(employee.Id, day, "rh");
                    adjustment.SetOvertimeConfiguration(true, false, "rh");
                    adjustments.Add(adjustment);
                }
            }
        }

        await using var context = _fixture.CreateDbContext();
        context.Branches.AddRange(branch, other);
        context.Departments.Add(department);
        context.Positions.Add(position);
        context.Shifts.Add(shift);
        context.Employees.AddRange(list);
        context.AttendanceRecords.AddRange(punches);
        context.AttendanceAdjustments.AddRange(adjustments);
        await context.SaveChangesAsync();
        return new Seeded(list, shift, branch.Id, other.Id);
    }

    private static AttendanceRecord Punch(EmployeeId employee, DateTime civil) =>
        AttendanceRecord.Create(employee, DeviceId.From("DEV-PG-BATCH"), DateTime.SpecifyKind(civil.AddHours(6), DateTimeKind.Utc), VerifyMethod.Fingerprint,
            CheckType.CheckIn, workDate: DateOnly.FromDateTime(civil), originalCheckTime: civil, timeZoneId: Zone, source: "Test");

    private static RepositoryCalculationReader RepositoryReader(AttendanceDbContext context) =>
        new(new EmployeeRepository(context), new DailyAttendanceRepository(context), new ShiftRosterRepository(context),
            new AttendanceAdjustmentRepository(context), new AttendanceRepository(context), new PunchAssignmentRepository(context));

    private ProcessDailyAttendanceCommandHandler Handler(AttendanceDbContext context, bool batchReader, int batchSize, IAttendanceCalculationLockService? lockService = null)
    {
        var dailyRepo = new DailyAttendanceRepository(context);
        var attendanceRepo = new AttendanceRepository(context);
        return new ProcessDailyAttendanceCommandHandler(
            dailyRepo, attendanceRepo, new EmployeeRepository(context), new ShiftRepository(context), new ShiftRosterRepository(context),
            new SystemConfigurationRepository(context), new AttendanceAdjustmentRepository(context), new PunchAssignmentRepository(context),
            lockService ?? new PostgresAttendanceCalculationLockService(context, NullLogger<PostgresAttendanceCalculationLockService>.Instance),
            context, new RegularSender(dailyRepo, attendanceRepo), NullLogger<ProcessDailyAttendanceCommandHandler>.Instance,
            reader: batchReader ? new EfAttendanceCalculationReader(context) : RepositoryReader(context),
            batchSize: batchSize);
    }

    [Fact]
    public async Task BatchReader_ReturnsTheSameDataAsTheRepositories()
    {
        if (!_fixture.IsAvailable) return;
        var seeded = await SeedAsync("RDA", 9, 3);
        var ids = seeded.Employees.Select(e => e.Id).ToList();

        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: true, batchSize: 25).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(2)), CancellationToken.None);
        }

        await using var verify = _fixture.CreateDbContext();
        var batch = new EfAttendanceCalculationReader(verify);
        var single = RepositoryReader(verify);

        var a = await batch.LoadWindowAsync(ids, Monday, Monday.AddDays(2), CancellationToken.None);
        var b = await single.LoadWindowAsync(ids, Monday, Monday.AddDays(2), CancellationToken.None);
        a.Dailies.Select(x => x.Id).Should().BeEquivalentTo(b.Dailies.Select(x => x.Id));
        a.Adjustments.Select(x => x.Id).Should().BeEquivalentTo(b.Adjustments.Select(x => x.Id));
        a.Records.Select(x => x.Id).Should().BeEquivalentTo(b.Records.Select(x => x.Id));
        a.Assignments.Select(x => x.Id).Distinct().Should().BeEquivalentTo(b.Assignments.Select(x => x.Id).Distinct());

        foreach (var (branch, employee) in new (BranchId?, EmployeeId?)[] { (null, null), (seeded.Branch, null), (seeded.OtherBranch, null), (null, ids[4]) })
        {
            var fromDb = (await batch.GetEmployeesActiveDuringAsync(Monday, Monday.AddDays(2), branch, employee, null, CancellationToken.None))
                .Where(e => ids.Contains(e.Id)).Select(e => e.Id);
            var fromRepo = (await single.GetEmployeesActiveDuringAsync(Monday, Monday.AddDays(2), branch, employee, null, CancellationToken.None))
                .Where(e => ids.Contains(e.Id)).Select(e => e.Id);
            fromDb.Should().BeEquivalentTo(fromRepo);
        }

        var inOther = (await batch.GetEmployeesActiveDuringAsync(Monday, Monday.AddDays(2), seeded.OtherBranch, null, null, CancellationToken.None)).Select(e => e.Id).ToList();
        inOther.Should().Contain(ids[1], "cambió a esta sucursal dentro del rango (vigencia)");
        inOther.Should().NotContain(ids[2], "dado de baja antes del rango");
    }

    [Fact]
    public async Task BatchedRecalculation_MatchesEmployeeByEmployee()
    {
        if (!_fixture.IsAvailable) return;
        var seeded = await SeedAsync("EQB", 7, 4);
        var ids = seeded.Employees.Select(e => e.Id).ToList();

        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: false, batchSize: 1).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(3)), CancellationToken.None);
        }
        var oneByOne = await SnapshotAsync(ids);

        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: true, batchSize: 3).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(3)), CancellationToken.None);
        }
        var batched = await SnapshotAsync(ids);

        batched.Should().Equal(oneByOne, "el lote no cambia ningún resultado");
        batched.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Cancelling_KeepsConfirmedBatches_RevertsTheCurrentOne_AndReleasesLocks()
    {
        if (!_fixture.IsAvailable) return;
        var seeded = await SeedAsync("CNL", 6, 2);
        var ids = seeded.Employees.Select(e => e.Id).ToList();
        using var cts = new CancellationTokenSource();

        await using (var context = _fixture.CreateDbContext())
        {
            var inner = new PostgresAttendanceCalculationLockService(context, NullLogger<PostgresAttendanceCalculationLockService>.Instance);
            var lockService = new CancelOnSecondBatchLock(inner, cts);
            var act = () => Handler(context, batchReader: true, batchSize: 2, lockService)
                .Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(1)), cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        await using var verify = _fixture.CreateDbContext();
        var calculated = await verify.DailyAttendances.Where(d => ids.Contains(d.EmployeeId)).Select(d => d.EmployeeId).Distinct().ToListAsync();
        var activeIds = ids.Where((_, i) => i != 2).OrderBy(e => e.Value, StringComparer.Ordinal).ToList();
        calculated.Should().BeEquivalentTo(activeIds.Take(2), "sólo el primer lote se confirmó; el progreso es recuperable");
        var advisory = await verify.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database())")
            .SingleAsync();
        advisory.Should().Be(0, "la cancelación liberó los bloqueos con la transacción");

        // Volver a calcular completa el resto.
        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: true, batchSize: 2).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(1)), CancellationToken.None);
        }
        await using var final = _fixture.CreateDbContext();
        (await final.DailyAttendances.Where(d => ids.Contains(d.EmployeeId)).Select(d => d.EmployeeId).Distinct().CountAsync()).Should().Be(activeIds.Count);
    }

    [Fact]
    public async Task RecalculatingWithoutChanges_WritesNothing_AndAChangeReplacesOnlyItsJornada()
    {
        if (!_fixture.IsAvailable) return;
        var seeded = await SeedAsync("WRT", 3, 3);
        var ids = seeded.Employees.Select(e => e.Id).ToList();

        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: true, batchSize: 25).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(2)), CancellationToken.None);
        }
        var before = await VersionsAsync(ids);

        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: true, batchSize: 25).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(2)), CancellationToken.None);
        }
        var unchanged = await VersionsAsync(ids);
        unchanged.Dailies.Should().Equal(before.Dailies, "sin cambios no se borra ni reinserta ninguna jornada (mismo Id y xmin)");
        unchanged.Records.Should().Equal(before.Records, "sin cambios no se reescribe ningún marcaje");

        // Un marcaje nuevo (salida más tardía) cambia sólo la jornada del lunes del primer empleado.
        var first = ids[0];
        await using (var context = _fixture.CreateDbContext())
        {
            context.AttendanceRecords.Add(Punch(first, Monday.AddHours(19)));
            await context.SaveChangesAsync();
        }
        await using (var context = _fixture.CreateDbContext())
        {
            await Handler(context, batchReader: true, batchSize: 25).Handle(new ProcessDailyAttendanceCommand(Monday, Monday.AddDays(2)), CancellationToken.None);
        }
        var after = await VersionsAsync(ids);
        var changedKey = $"{first.Value}|{Monday:yyyy-MM-dd}";
        after.Dailies.Where(d => !d.StartsWith(changedKey)).Should().Equal(before.Dailies.Where(d => !d.StartsWith(changedKey)));
        after.Dailies.Single(d => d.StartsWith(changedKey)).Should().NotBe(before.Dailies.Single(d => d.StartsWith(changedKey)));

        await using var verify = _fixture.CreateDbContext();
        var monday = await verify.DailyAttendances.SingleAsync(d => d.EmployeeId == first && d.Date == Monday);
        monday.ActualCheckOut.Should().Be(Monday.AddHours(19 + 6), "la jornada reemplazada refleja el marcaje nuevo");
    }

    private async Task<(List<string> Dailies, List<string> Records)> VersionsAsync(IReadOnlyCollection<EmployeeId> ids)
    {
        // xmin: versión de fila de PostgreSQL; cambia con cualquier UPDATE o con un DELETE + INSERT.
        await using var context = _fixture.CreateDbContext();
        var keys = ids.Select(i => i.Value).ToArray();
        var dailies = await context.Database.SqlQueryRaw<string>(
            "SELECT \"EmployeeId\" || '|' || to_char(\"Date\", 'YYYY-MM-DD') || '|' || \"Id\"::text || '|' || xmin::text AS \"Value\" " +
            "FROM \"DailyAttendances\" WHERE \"EmployeeId\" = ANY({0}) ORDER BY \"EmployeeId\", \"Date\"", new object[] { keys }).ToListAsync();
        var records = await context.Database.SqlQueryRaw<string>(
            "SELECT \"Id\"::text || '|' || xmin::text AS \"Value\" FROM \"AttendanceRecords\" WHERE \"EmployeeId\" = ANY({0}) ORDER BY \"EmployeeId\", \"CheckTime\"", new object[] { keys }).ToListAsync();
        return (dailies, records);
    }

    private async Task<List<string>> SnapshotAsync(IReadOnlyCollection<EmployeeId> ids)
    {
        await using var context = _fixture.CreateDbContext();
        var dailies = await context.DailyAttendances.AsNoTracking().Where(d => ids.Contains(d.EmployeeId))
            .OrderBy(d => d.EmployeeId).ThenBy(d => d.Date).ToListAsync();
        var assignments = await context.PunchAssignments.AsNoTracking().Where(a => ids.Contains(a.EmployeeId))
            .OrderBy(a => a.EmployeeId).ThenBy(a => a.WorkDate).ThenBy(a => a.Id).ToListAsync();
        return dailies.Select(d => $"{d.EmployeeId.Value}|{d.Date:yyyy-MM-dd}|{d.CheckInRecordId?.Value}|{d.CheckOutRecordId?.Value}|{d.LateMinutes}|{d.OvertimeMinutes}|{d.RawOvertimeMinutes}|{d.TotalWorkedMinutes}|{d.IsAbsent}")
            .Concat(assignments.Select(a => $"{a.EmployeeId.Value}|{a.WorkDate}|{a.Id.Value}|{a.Role}|{a.Origin}"))
            .ToList();
    }

    /// <summary>Cancela el recálculo al empezar el segundo lote (el primero ya se confirmó).</summary>
    private sealed class CancelOnSecondBatchLock : IAttendanceCalculationLockService
    {
        private readonly IAttendanceCalculationLockService _inner;
        private readonly CancellationTokenSource _cts;
        private int _calls;

        public CancelOnSecondBatchLock(IAttendanceCalculationLockService inner, CancellationTokenSource cts)
        {
            _inner = inner;
            _cts = cts;
        }

        public async Task AcquireAsync(IEnumerable<EmployeeId> employeeIds, CancellationToken cancellationToken = default)
        {
            await _inner.AcquireAsync(employeeIds, cancellationToken);
            if (Interlocked.Increment(ref _calls) == 2) _cts.Cancel();
        }
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
}
