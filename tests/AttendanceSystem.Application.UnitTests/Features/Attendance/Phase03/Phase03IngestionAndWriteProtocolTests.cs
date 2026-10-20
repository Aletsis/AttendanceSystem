using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase03;

/// <summary>
/// F03-05 / F03-06 / F03-07: ingesta idempotente y protocolo común de escritura, sin base de datos.
/// Las garantías relacionales reales están en Infrastructure.Tests/Persistence/PostgresConcurrencyTests.
/// </summary>
public class Phase03IngestionAndWriteProtocolTests
{
    private sealed class FixedClassifier : IPersistenceFailureClassifier
    {
        private readonly PersistenceFailureKind _kind;
        public FixedClassifier(PersistenceFailureKind kind) => _kind = kind;
        public PersistenceFailure Classify(Exception exception) =>
            exception is OperationCanceledException
                ? new PersistenceFailure(PersistenceFailureKind.Cancelled, null, exception.Message)
                : new PersistenceFailure(_kind, "IX_AttendanceRecords_EmployeeId_CheckTime", exception.Message);
    }

    private static AttendanceRecord Punch(string employee, int minute) =>
        AttendanceRecord.Create(EmployeeId.From(employee), DeviceId.From("DEV-U-01"),
            new DateTime(2026, 6, 1, 14, minute, 0, DateTimeKind.Utc), VerifyMethod.Fingerprint, CheckType.CheckIn);

    [Fact]
    public async Task UniqueViolation_ConfirmsTheExistingRow_DetachesPending_AndRetriesOnlyTheRest()
    {
        var repo = new Mock<IAttendanceRepository>();
        var uow = new Mock<IUnitOfWork>();
        var winner = Punch("EMP-U-01", 0);
        var batch = new[] { Punch("EMP-U-01", 0), Punch("EMP-U-01", 1), Punch("EMP-U-01", 2) };

        // 1ª consulta: nada existe; tras perder la carrera, la fila del ganador aparece.
        repo.SetupSequence(r => r.GetByIdentitiesAsync(It.IsAny<IReadOnlyCollection<(EmployeeId, DateTime)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>())
            .ReturnsAsync(new List<AttendanceRecord> { winner });
        var added = new List<List<AttendanceRecord>>();
        repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<AttendanceRecord>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AttendanceRecord>, CancellationToken>((records, _) => added.Add(records.ToList()))
            .Returns(Task.CompletedTask);
        uow.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("23505"))
            .ReturnsAsync(2);

        var ingestion = new AttendanceRecordIngestion(repo.Object, uow.Object, new FixedClassifier(PersistenceFailureKind.UniqueViolation));
        var result = await ingestion.IngestAsync(batch, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Inserted.Should().HaveCount(2);
        result.ConfirmedDuplicates.Should().ContainKey((winner.EmployeeId, winner.CheckTime))
            .WhoseValue.Should().Be(winner.Id, "el duplicado devuelve el identificador de la fila existente");
        added[1].Should().HaveCount(2, "el reintento no incluye la colisión confirmada");
        repo.Verify(r => r.Detach(It.IsAny<AttendanceRecord>()), Times.Exactly(3), "las entidades fallidas dejan de rastrearse antes de reintentar");
    }

    [Theory]
    [InlineData(PersistenceFailureKind.Connection)]
    [InlineData(PersistenceFailureKind.Timeout)]
    [InlineData(PersistenceFailureKind.InvalidData)]
    [InlineData(PersistenceFailureKind.Unknown)]
    public async Task NonUniqueFailures_AreReported_AndNeverCountedAsDuplicates(PersistenceFailureKind kind)
    {
        var repo = new Mock<IAttendanceRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("fallo"));

        var ingestion = new AttendanceRecordIngestion(repo.Object, uow.Object, new FixedClassifier(kind));
        var result = await ingestion.IngestAsync(new[] { Punch("EMP-U-02", 0), Punch("EMP-U-02", 1) }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.Kind.Should().Be(kind);
        result.Inserted.Should().BeEmpty();
        result.ConfirmedDuplicates.Should().BeEmpty();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once, "sólo una colisión de identidad justifica reintentar");
    }

    [Fact]
    public async Task Cancellation_Propagates_AfterLeavingTheContextClean()
    {
        var repo = new Mock<IAttendanceRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        var ingestion = new AttendanceRecordIngestion(repo.Object, uow.Object, new FixedClassifier(PersistenceFailureKind.Unknown));
        var act = () => ingestion.IngestAsync(new[] { Punch("EMP-U-03", 0) }, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        repo.Verify(r => r.Detach(It.IsAny<AttendanceRecord>()), Times.Once);
    }

    [Fact]
    public async Task InBatchDuplicates_AreCollapsed_BeforeTouchingTheDatabase()
    {
        var repo = new Mock<IAttendanceRepository>();
        var uow = new Mock<IUnitOfWork>();
        var ingestion = new AttendanceRecordIngestion(repo.Object, uow.Object);

        var result = await ingestion.IngestAsync(new[] { Punch("EMP-U-04", 0), Punch("EMP-U-04", 0), Punch("EMP-U-04", 5) }, CancellationToken.None);

        result.InBatchDuplicates.Should().Be(1);
        result.Inserted.Should().HaveCount(2);
    }

    [Fact]
    public async Task WriteProtocol_Commits_OnlyAfterAcquiringTheLock()
    {
        var order = new List<string>();
        var uow = new Mock<IUnitOfWork>();
        var locks = new Mock<IAttendanceCalculationLockService>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("begin")).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        locks.Setup(l => l.AcquireAsync(It.IsAny<IEnumerable<EmployeeId>>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("lock")).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("commit")).Returns(Task.CompletedTask);

        var result = await ExclusiveAttendanceWrite.RunAsync(uow.Object, locks.Object, new[] { EmployeeId.From("EMP-U-05") },
            () => { order.Add("work"); return Task.FromResult(Result.Success()); }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Should().Equal("begin", "lock", "work", "commit");
    }

    [Fact]
    public async Task WriteProtocol_OnFailureOrCancellation_RollsBackWithANonCancelledToken_AndDiscardsChanges()
    {
        var uow = new Mock<IUnitOfWork>();
        CancellationToken rollbackToken = new(canceled: true);
        uow.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(t => rollbackToken = t)
            .Returns(Task.CompletedTask);
        using var cts = new CancellationTokenSource();

        var act = () => ExclusiveAttendanceWrite.RunAsync<int>(uow.Object, Mock.Of<IAttendanceCalculationLockService>(), new[] { EmployeeId.From("EMP-U-06") },
            () => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }, _ => true, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rollbackToken.IsCancellationRequested.Should().BeFalse("el rollback debe ejecutarse aunque el token de la operación esté cancelado");
        uow.Verify(u => u.DiscardChanges(), Times.Once, "no se reutiliza un contexto con estado incoherente");
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteProtocol_FailedResult_RollsBackInsteadOfCommitting()
    {
        var uow = new Mock<IUnitOfWork>();

        var result = await ExclusiveAttendanceWrite.RunAsync(uow.Object, Mock.Of<IAttendanceCalculationLockService>(), new[] { EmployeeId.From("EMP-U-07") },
            () => Task.FromResult(Result.Failure("rechazado")), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
