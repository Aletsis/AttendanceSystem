using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Features.Attendance.Consolidation;
using AttendanceSystem.Application.Features.Attendance.EventHandlers;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Events;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Outbox;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// Outbox y consolidación durable sobre PostgreSQL real: interrupción en cada frontera de commit,
/// consumidores concurrentes, ampliación durante el cálculo, fallos persistentes, agrupación y rollback.
/// Las pruebas de la clase comparten base de datos y se ejecutan en serie; cada una limpia sus tablas.
/// </summary>
public class PostgresConsolidationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Zone = "America/Mexico_City";
    private static readonly TimeSpan UtcMinus6 = TimeSpan.FromHours(-6);
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresConsolidationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static ConsolidationOptions Immediate() => new()
    {
        QuietPeriod = TimeSpan.Zero,
        MaxDelay = TimeSpan.FromHours(1),
        Lease = TimeSpan.FromMinutes(5),
        BatchSize = 50,
        MaxAttempts = 3,
        BaseBackoff = TimeSpan.Zero
    };

    #region 1 y 7: fronteras de commit del outbox

    [Fact]
    public async Task CrashAfterSavingThePunch_BeforeRequestingConsolidation_IsRecoveredByTheWorkers()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var punch = Punch("EMP-PG-K01", new DateTime(2026, 5, 4, 8, 0, 0));

        // El marcaje y su evento se confirman, pero el proceso "muere" antes de publicar: se confirma la
        // transacción sin pasar por el protocolo del contexto y el lease del mensaje vence.
        await using (var context = _fixture.CreateDbContext())
        {
            await using var tx = await context.Database.BeginTransactionAsync();
            context.AttendanceRecords.Add(punch);
            await context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await ExpireOutboxLeasesAsync();
        (await CountActiveRequestsAsync("EMP-PG-K01")).Should().Be(0, "precondición: nadie solicitó la consolidación");

        using var services = BuildServices(new RecordingRecalculation());
        var outbox = new OutboxBackgroundProcessor(services, NullLogger<OutboxBackgroundProcessor>.Instance);
        (await outbox.ProcessPendingMessagesAsync()).Should().Be(1);

        (await CountActiveRequestsAsync("EMP-PG-K01")).Should().Be(1, "el worker del outbox publicó el evento y se creó la solicitud");

        var recalculation = (RecordingRecalculation)services.GetRequiredService<RecordingRecalculation>();
        var worker = CreateWorker(services, Immediate());
        (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(1);

        recalculation.Commands.Should().ContainSingle(c => c.EmployeeId == EmployeeId.From("EMP-PG-K01") && c.StartDate == new DateTime(2026, 5, 4));
        (await CountActiveRequestsAsync("EMP-PG-K01")).Should().Be(0, "la solicitud se completó");
    }

    [Fact]
    public async Task RollbackOfTheEnclosingTransaction_PublishesNothing_AndLeavesNoMessages()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var publisher = new Mock<IPublisher>();
        await using (var context = _fixture.CreateDbContext(publisher.Object))
        {
            IUnitOfWork uow = context;
            await using var tx = await uow.BeginTransactionAsync();
            context.AttendanceRecords.Add(Punch("EMP-PG-K02", new DateTime(2026, 5, 5, 8, 0, 0)));
            await uow.SaveChangesAsync();

            publisher.Invocations.Should().BeEmpty("dentro de la transacción no hay efectos externos");
            await uow.RollbackTransactionAsync();
        }

        publisher.Invocations.Should().BeEmpty("la transacción revertida no deja trabajos que dependan de datos inexistentes");
        await using var verify = _fixture.CreateDbContext();
        (await verify.OutboxMessages.CountAsync()).Should().Be(0);
        (await verify.AttendanceRecords.CountAsync(r => r.EmployeeId == EmployeeId.From("EMP-PG-K02"))).Should().Be(0);
    }

    [Fact]
    public async Task CommitOfTheEnclosingTransaction_PublishesOnlyAfterCommit_AndExactlyOnce()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var publisher = new Mock<IPublisher>();
        await using (var context = _fixture.CreateDbContext(publisher.Object))
        {
            IUnitOfWork uow = context;
            await using var tx = await uow.BeginTransactionAsync();
            context.AttendanceRecords.Add(Punch("EMP-PG-K03", new DateTime(2026, 5, 6, 8, 0, 0)));
            await uow.SaveChangesAsync();
            publisher.Invocations.Should().BeEmpty();

            await uow.CommitTransactionAsync();
        }

        publisher.Verify(p => p.Publish(It.Is<INotification>(n => n is AttendanceRecordedEvent), It.IsAny<CancellationToken>()), Times.Once);

        await using var verify = _fixture.CreateDbContext();
        var message = await verify.OutboxMessages.SingleAsync(m => m.Type.Contains(nameof(AttendanceRecordedEvent)));
        message.ProcessedOnUtc.Should().NotBeNull();
        message.LockedBy.Should().BeNull();

        // El worker no vuelve a publicar un mensaje ya procesado.
        using var services = BuildServices(new RecordingRecalculation());
        (await new OutboxBackgroundProcessor(services, NullLogger<OutboxBackgroundProcessor>.Instance).ProcessPendingMessagesAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImmediatePathClaim_IsRespectedByTheWorker_UntilItsLeaseExpires()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        await using (var context = _fixture.CreateDbContext())
        {
            await using var tx = await context.Database.BeginTransactionAsync();
            context.AttendanceRecords.Add(Punch("EMP-PG-K04", new DateTime(2026, 5, 7, 8, 0, 0)));
            await context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        using var services = BuildServices(new RecordingRecalculation());
        var outbox = new OutboxBackgroundProcessor(services, NullLogger<OutboxBackgroundProcessor>.Instance);
        (await outbox.ProcessPendingMessagesAsync()).Should().Be(0, "el mensaje sigue reclamado por quien lo guardó: no hay doble publicación");

        await ExpireOutboxLeasesAsync();
        (await outbox.ProcessPendingMessagesAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OutboxFailures_BackOff_ThenDeadLetter_AndCanBeRevived()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var failing = new Mock<IPublisher>();
        failing.Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("destino caído"));
        await using (var context = _fixture.CreateDbContext(failing.Object))
        {
            context.AttendanceRecords.Add(Punch("EMP-PG-K05", new DateTime(2026, 5, 8, 8, 0, 0)));
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var message = await context.OutboxMessages.SingleAsync(m => m.Type.Contains(nameof(AttendanceRecordedEvent)));
            message.RetryCount.Should().Be(1);
            message.NextAttemptOnUtc.Should().BeAfter(DateTime.UtcNow, "el reintento espera el backoff");
            message.Error.Should().Contain("destino caído");
        }

        using var services = BuildServices(new RecordingRecalculation(), failing.Object);
        var outbox = new OutboxBackgroundProcessor(services, NullLogger<OutboxBackgroundProcessor>.Instance);
        for (int attempt = 2; attempt <= OutboxMessageAttempts; attempt++)
        {
            await ExecuteAsync(@"UPDATE ""OutboxMessages"" SET ""NextAttemptOnUtc"" = now() - interval '1 second'");
            await outbox.ProcessPendingMessagesAsync();
        }

        var monitor = new OutboxStore(_fixture.CreateDbContext());
        var health = await monitor.GetHealthAsync();
        health.DeadLettered.Should().Be(1, "tras agotar intentos queda visible como mensaje muerto");
        health.Pending.Should().Be(0);
        health.RecentDeadLetters.Single().Error.Should().Contain("destino caído");

        (await monitor.ReviveDeadLettersAsync()).Should().Be(1);
        (await outbox.ProcessPendingMessagesAsync()).Should().Be(0, "sigue fallando, pero vuelve a intentarse");
        (await new OutboxStore(_fixture.CreateDbContext()).GetHealthAsync()).DeadLettered.Should().Be(0);
    }

    private const int OutboxMessageAttempts = AttendanceSystem.Domain.Entities.OutboxMessage.DefaultMaxAttempts;

    #endregion

    #region 2: caída después de calcular y antes de completar

    [Fact]
    public async Task CrashAfterCalculating_BeforeCompleting_RepeatingDoesNotChangeResults()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var employee = CreateEmployee("EMP-PG-K06");
        var day = new DateTime(2026, 5, 11);
        await SeedAsync(Punch(employee.Id.Value, day.AddHours(8)), Punch(employee.Id.Value, day.AddHours(16)));
        await RequestAsync(employee.Id, day, day);

        var recalculation = new RealRecalculation(_fixture, employee);
        using var services = BuildServices(recalculation);

        // El consumidor calcula y confirma el resultado, pero muere antes de completar la solicitud.
        IReadOnlyList<ConsolidationClaim> claims;
        await using (var context = _fixture.CreateDbContext())
        {
            claims = await new AttendanceConsolidationRepository(context).ClaimAsync("crashed", 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1));
        }
        claims.Should().ContainSingle();
        await recalculation.RunAsync(claims[0]);
        var first = await SnapshotAsync(employee.Id);

        await ExecuteAsync(@"UPDATE ""AttendanceConsolidationRequests"" SET ""LeaseExpiresAt"" = now() - interval '1 second'");

        var worker = CreateWorker(services, Immediate());
        (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(1, "el lease vencido devuelve la solicitud a otro consumidor");

        (await SnapshotAsync(employee.Id)).Should().Equal(first, "repetir el recálculo es idempotente");
        (await CountActiveRequestsAsync(employee.Id.Value)).Should().Be(0);
        recalculation.Runs.Should().Be(2);
    }

    #endregion

    #region 3, 4 y F04-04: reclamación concurrente, ampliación durante el cálculo y unicidad de activas

    [Fact]
    public async Task TwoWorkersClaimingTheSameSet_GetDisjointClaims_AndNothingIsLost()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var employees = Enumerable.Range(1, 12).Select(i => EmployeeId.From($"EMP-PG-W{i:00}")).ToList();
        foreach (var employee in employees) await RequestAsync(employee, new DateTime(2026, 5, 12), new DateTime(2026, 5, 12));

        async Task<IReadOnlyList<ConsolidationClaim>> ClaimAsync(string consumer, int limit)
        {
            await using var context = _fixture.CreateDbContext();
            return await new AttendanceConsolidationRepository(context).ClaimAsync(consumer, limit, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1));
        }

        var results = await Task.WhenAll(ClaimAsync("worker-a", 8), ClaimAsync("worker-b", 8));

        var a = results[0].Select(c => c.Id).ToHashSet();
        var b = results[1].Select(c => c.Id).ToHashSet();
        a.Overlaps(b).Should().BeFalse("SKIP LOCKED reparte conjuntos disjuntos");
        (a.Count + b.Count).Should().Be(12, "entre ambos reclaman todas las solicitudes, sin perder ninguna");
        (await ClaimAsync("worker-c", 50)).Should().BeEmpty("mientras dura el lease nadie más las toma");
    }

    [Fact]
    public async Task RequestExpandedDuringTheCalculation_KeepsTheNewDatesPending()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var employee = EmployeeId.From("EMP-PG-K08");
        await RequestAsync(employee, new DateTime(2026, 5, 13), new DateTime(2026, 5, 13));

        await using var context = _fixture.CreateDbContext();
        var repository = new AttendanceConsolidationRepository(context);
        var claim = (await repository.ClaimAsync("worker-a", 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1))).Single();

        // Durante el cálculo llega un marcaje del día 15.
        await RequestAsync(employee, new DateTime(2026, 5, 15), new DateTime(2026, 5, 15));

        (await repository.CompleteAsync(claim.Id, "worker-a", claim.Version)).Should().Be(ConsolidationCompletion.Requeued);

        var pending = (await repository.GetActiveByEmployeeAsync(employee))!;
        pending.Status.Should().Be(ConsolidationStatus.Pending, "la versión nueva queda pendiente hasta procesarse (A07)");
        pending.StartDate.Should().Be(new DateTime(2026, 5, 13));
        pending.EndDate.Should().Be(new DateTime(2026, 5, 15));

        var second = (await repository.ClaimAsync("worker-a", 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1))).Single();
        second.Version.Should().BeGreaterThan(claim.Version);
        (await repository.CompleteAsync(second.Id, "worker-a", second.Version)).Should().Be(ConsolidationCompletion.Completed);
        (await repository.GetActiveByEmployeeAsync(employee)).Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSameEmployee_KeepASingleActiveRow_WithTheUnionOfRanges()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var employee = EmployeeId.From("EMP-PG-K09");
        var dates = Enumerable.Range(0, 20).Select(i => new DateTime(2026, 5, 1).AddDays(i)).ToList();

        // Veinte conexiones amplían a la vez (lectura-modificación-escritura atómica en una sentencia).
        await Task.WhenAll(dates.Select(d => Task.Run(() => RequestAsync(employee, d, d))));

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify.AttendanceConsolidationRequests.Where(r => r.EmployeeId == employee).ToListAsync();
        rows.Should().ContainSingle("una sola solicitud activa por empleado");
        rows[0].StartDate.Should().Be(dates.First());
        rows[0].EndDate.Should().Be(dates.Last());
        rows[0].Version.Should().Be(20, "cada ampliación incrementa la versión; ninguna se pierde");
    }

    [Fact]
    public async Task UnfinishedLease_IsNotCompletedByTheConsumerThatLostIt()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var employee = EmployeeId.From("EMP-PG-K10");
        await RequestAsync(employee, new DateTime(2026, 5, 14), new DateTime(2026, 5, 14));
        await using var context = _fixture.CreateDbContext();
        var repository = new AttendanceConsolidationRepository(context);

        var slow = (await repository.ClaimAsync("slow", 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1))).Single();
        await ExecuteAsync(@"UPDATE ""AttendanceConsolidationRequests"" SET ""LeaseExpiresAt"" = now() - interval '1 second'");
        var fast = (await repository.ClaimAsync("fast", 10, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromHours(1))).Single();

        (await repository.CompleteAsync(slow.Id, "slow", slow.Version)).Should().Be(ConsolidationCompletion.LeaseLost);
        (await repository.GetActiveByEmployeeAsync(employee))!.ClaimedBy.Should().Be("fast");
        (await repository.CompleteAsync(fast.Id, "fast", fast.Version)).Should().Be(ConsolidationCompletion.Completed);
    }

    #endregion

    #region 5: fallos persistentes visibles y reintentables

    [Fact]
    public async Task PersistentFailure_BecomesVisible_AndCanBeRetriedAfterFixingTheCause()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        var employee = EmployeeId.From("EMP-PG-K11");
        await RequestAsync(employee, new DateTime(2026, 5, 18), new DateTime(2026, 5, 18));

        var recalculation = new RecordingRecalculation { Failure = new InvalidOperationException("turno inexistente") };
        using var services = BuildServices(recalculation);
        var options = Immediate();
        var worker = CreateWorker(services, options);

        for (int i = 0; i < options.MaxAttempts; i++)
        {
            await worker.DrainOnceAsync(CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var health = await new AttendanceConsolidationRepository(context).GetHealthAsync();
            health.Failed.Should().Be(1);
            health.Pending.Should().Be(0);
            health.RecentFailures.Single().ErrorMessage.Should().Contain("turno inexistente");
            health.RecentFailures.Single().RetryCount.Should().Be(options.MaxAttempts);
        }
        (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(0, "un fallo terminal no se reintenta solo");

        // Se corrige la causa y se reintenta desde la operación.
        recalculation.Failure = null;
        await using (var context = _fixture.CreateDbContext())
        {
            (await new AttendanceConsolidationRepository(context).RetryFailedAsync()).Should().Be(1);
        }
        (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(1);

        await using (var context = _fixture.CreateDbContext())
        {
            var health = await new AttendanceConsolidationRepository(context).GetHealthAsync();
            health.Failed.Should().Be(0);
            health.Pending.Should().Be(0);
        }
        recalculation.Commands.Last().EmployeeId.Should().Be(employee);
    }

    #endregion

    #region 6: agrupación y límite de antigüedad

    [Fact]
    public async Task MassImport_IsGroupedPerEmployee_AndNoEmployeeIsPostponedIndefinitely()
    {
        if (!_fixture.IsAvailable) return;
        await ResetAsync();

        // Importación masiva: 300 marcajes de 3 empleados en 10 días.
        var employees = new[] { "EMP-PG-M1", "EMP-PG-M2", "EMP-PG-M3" }.Select(EmployeeId.From).ToList();
        for (int i = 0; i < 300; i++)
        {
            var day = new DateTime(2026, 5, 20).AddDays(i % 10);
            await RequestAsync(employees[i % 3], day, day);
        }

        await using var context = _fixture.CreateDbContext();
        (await context.AttendanceConsolidationRequests.CountAsync()).Should().Be(3, "una solicitud agrupada por empleado, no una por marcaje");

        var repository = new AttendanceConsolidationRepository(context);
        var quiet = TimeSpan.FromHours(1);
        var maxDelay = TimeSpan.FromMinutes(10);

        (await repository.ClaimAsync("w", 10, TimeSpan.FromMinutes(5), quiet, maxDelay))
            .Should().BeEmpty("mientras siguen llegando marcajes se espera para agruparlos");

        // Un empleado con flujo continuo: su primera solicitud ya superó la antigüedad máxima.
        await ExecuteAsync(@"UPDATE ""AttendanceConsolidationRequests"" SET ""RequestedAt"" = now() - interval '11 minutes' WHERE ""EmployeeId"" = 'EMP-PG-M2'");
        await RequestAsync(employees[1], new DateTime(2026, 5, 30), new DateTime(2026, 5, 30));

        var claims = await repository.ClaimAsync("w", 10, TimeSpan.FromMinutes(5), quiet, maxDelay);
        claims.Should().ContainSingle(c => c.EmployeeId == employees[1], "la antigüedad máxima impide postergarlo indefinidamente");
        claims[0].StartDate.Should().Be(new DateTime(2026, 5, 20));
        claims[0].EndDate.Should().Be(new DateTime(2026, 5, 30));
    }

    [Fact]
    public async Task WakeupSignals_AreCoalesced()
    {
        var signal = new ConsolidationWakeupSignal();
        for (int i = 0; i < 1000; i++) signal.Signal();

        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();
        (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse("mil avisos producen una sola revisión");
    }

    #endregion

    #region Infraestructura de prueba

    private static AttendanceRecord Punch(string employeeId, DateTime civil) =>
        AttendanceRecord.Create(EmployeeId.From(employeeId), DeviceId.From("DEV-PG-K"), DateTime.SpecifyKind(civil - UtcMinus6, DateTimeKind.Utc),
            VerifyMethod.Fingerprint, CheckType.CheckIn, workDate: DateOnly.FromDateTime(civil), originalCheckTime: civil, timeZoneId: Zone, source: "Test");

    private static Employee CreateEmployee(string id) =>
        Employee.Create(EmployeeId.From(id), "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Matutino, null, restDays: Array.Empty<WeekDay>());

    private async Task ResetAsync() =>
        await ExecuteAsync(@"DELETE FROM ""AttendanceConsolidationRequests""; DELETE FROM ""OutboxMessages"";");

    private async Task ExecuteAsync(string sql)
    {
        await using var context = _fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private Task ExpireOutboxLeasesAsync() =>
        ExecuteAsync(@"UPDATE ""OutboxMessages"" SET ""LockedUntilUtc"" = now() - interval '1 second' WHERE ""LockedUntilUtc"" IS NOT NULL");

    private async Task RequestAsync(EmployeeId employee, DateTime start, DateTime end)
    {
        await using var context = _fixture.CreateDbContext();
        await new AttendanceConsolidationRepository(context).RequestAsync(employee, start, end);
    }

    private async Task SeedAsync(params AttendanceRecord[] records)
    {
        await using var context = _fixture.CreateDbContext();
        context.AttendanceRecords.AddRange(records);
        await context.SaveChangesAsync();
    }

    private async Task<int> CountActiveRequestsAsync(string employeeId)
    {
        await using var context = _fixture.CreateDbContext();
        return await context.AttendanceConsolidationRequests.CountAsync(r =>
            r.EmployeeId == EmployeeId.From(employeeId) && (r.Status == ConsolidationStatus.Pending || r.Status == ConsolidationStatus.Processing));
    }

    private async Task<List<string>> SnapshotAsync(EmployeeId employee)
    {
        await using var context = _fixture.CreateDbContext();
        var dailies = await context.DailyAttendances.Where(d => d.EmployeeId == employee).OrderBy(d => d.Date).ToListAsync();
        var rows = await context.PunchAssignments.Where(a => a.EmployeeId == employee).OrderBy(a => a.WorkDate).ThenBy(a => a.Role).ToListAsync();
        return dailies.Select(d => $"{d.Date:yyyy-MM-dd}|{d.CheckInRecordId}|{d.CheckOutRecordId}|{d.LateMinutes}|{d.TotalWorkedMinutes}|{d.OvertimeMinutes}")
            .Concat(rows.Select(r => $"{r.AttendanceRecordId}|{r.WorkDate}|{r.Role}|{r.Origin}"))
            .ToList();
    }

    /// <summary>Contenedor como el del servidor: contexto por ámbito, repositorio, procesador y publicador real.</summary>
    private ServiceProvider BuildServices(IRecalculation recalculation, IPublisher? publisher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _fixture.CreateDbContext(publisher));
        services.AddScoped<IAttendanceConsolidationRepository, AttendanceConsolidationRepository>();
        services.AddScoped<AttendanceConsolidationProcessor>();
        services.AddSingleton(recalculation);
        if (recalculation is RecordingRecalculation recording) services.AddSingleton(recording);
        services.AddScoped<ISender>(sp => new RecalculationSender(sp.GetRequiredService<IRecalculation>()));
        if (publisher == null)
        {
            // Publicador real mínimo: entrega AttendanceRecordedEvent a su manejador con el contexto del ámbito.
            services.AddScoped<IPublisher>(sp => new RecordedEventPublisher(sp.GetRequiredService<IAttendanceConsolidationRepository>()));
        }
        else
        {
            services.AddSingleton(publisher);
        }
        return services.BuildServiceProvider();
    }

    private static AttendanceConsolidationWorker CreateWorker(ServiceProvider services, ConsolidationOptions options) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), new ConsolidationWakeupSignal(), options, NullLogger<AttendanceConsolidationWorker>.Instance);

    private interface IRecalculation
    {
        Task RunAsync(ProcessDailyAttendanceCommand command);
    }

    private sealed class RecordingRecalculation : IRecalculation
    {
        public ConcurrentQueue<ProcessDailyAttendanceCommand> Commands { get; } = new();
        public Exception? Failure { get; set; }

        public Task RunAsync(ProcessDailyAttendanceCommand command)
        {
            Commands.Enqueue(command);
            return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    /// <summary>Recálculo real (orquestador y subcomandos) sobre su propio contexto.</summary>
    private sealed class RealRecalculation : IRecalculation
    {
        private readonly PostgresTestDatabaseFixture _fixture;
        private readonly Employee _employee;
        public int Runs { get; private set; }

        public RealRecalculation(PostgresTestDatabaseFixture fixture, Employee employee)
        {
            _fixture = fixture;
            _employee = employee;
        }

        public Task RunAsync(ConsolidationClaim claim) => RunAsync(new ProcessDailyAttendanceCommand(claim.StartDate, claim.EndDate, EmployeeId: claim.EmployeeId));

        public async Task RunAsync(ProcessDailyAttendanceCommand command)
        {
            Runs++;
            await using var context = _fixture.CreateDbContext();
            var regular = Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);
            var rosters = new List<ShiftRoster>();
            for (var d = command.StartDate.Date.AddDays(-1); d <= command.EndDate.Date.AddDays(1); d = d.AddDays(1))
                rosters.Add(ShiftRoster.Create(_employee.Id, d, regular.Id, isRestDay: false));

            var employeeRepo = new Mock<IEmployeeRepository>();
            employeeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
            var shiftRepo = new Mock<IShiftRepository>();
            shiftRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { regular });
            var rosterRepo = new Mock<IShiftRosterRepository>();
            rosterRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateTime s, DateTime e, BranchId? _, EmployeeId? _, CancellationToken _) => rosters.Where(r => r.Date >= s.Date && r.Date <= e.Date).ToList());
            var configRepo = new Mock<ISystemConfigurationRepository>();
            configRepo.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync((SystemConfiguration?)null);

            var dailyRepo = new DailyAttendanceRepository(context);
            var attendanceRepo = new AttendanceRepository(context);
            var handler = new ProcessDailyAttendanceCommandHandler(
                dailyRepo, attendanceRepo, employeeRepo.Object, shiftRepo.Object, rosterRepo.Object, configRepo.Object,
                new AttendanceAdjustmentRepository(context), new PunchAssignmentRepository(context),
                new PostgresAttendanceCalculationLockService(context, NullLogger<PostgresAttendanceCalculationLockService>.Instance),
                context, new RegularOnlySender(dailyRepo, attendanceRepo), NullLogger<ProcessDailyAttendanceCommandHandler>.Instance);

            await handler.Handle(command, CancellationToken.None);
        }
    }

    private sealed class RecalculationSender : ISender
    {
        private readonly IRecalculation _recalculation;
        public RecalculationSender(IRecalculation recalculation) => _recalculation = recalculation;

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not ProcessDailyAttendanceCommand command) throw new NotSupportedException(request.GetType().Name);
            await _recalculation.RunAsync(command);
            return default!;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RegularOnlySender : ISender
    {
        private readonly IDailyAttendanceRepository _dailyRepo;
        private readonly IAttendanceRepository _attendanceRepo;

        public RegularOnlySender(IDailyAttendanceRepository dailyRepo, IAttendanceRepository attendanceRepo)
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

    private sealed class RecordedEventPublisher : IPublisher
    {
        private readonly AttendanceRecordedEventHandler _handler;

        public RecordedEventPublisher(IAttendanceConsolidationRepository repository) =>
            _handler = new AttendanceRecordedEventHandler(repository, NoOpConsolidationWakeup.Instance, NullLogger<AttendanceRecordedEventHandler>.Instance);

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            notification is AttendanceRecordedEvent e ? _handler.Handle(e, cancellationToken) : Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification =>
            Publish((object)notification!, cancellationToken);
    }

    #endregion
}
