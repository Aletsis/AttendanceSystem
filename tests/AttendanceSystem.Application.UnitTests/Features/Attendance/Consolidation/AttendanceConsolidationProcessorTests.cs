using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Consolidation;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Consolidation;

public class AttendanceConsolidationProcessorTests
{
    private const string Consumer = "consumer-1";
    private readonly Mock<IAttendanceConsolidationRepository> _repo = new();
    private readonly Mock<ISender> _sender = new();
    private readonly ConsolidationOptions _options = new() { MaxAttempts = 3, BaseBackoff = TimeSpan.FromSeconds(10) };
    private readonly ConsolidationClaim _claim = new(Guid.NewGuid(), EmployeeId.From("EMP-CP-1"), new DateTime(2026, 6, 1), new DateTime(2026, 6, 3), Version: 4, Attempts: 1);

    private AttendanceConsolidationProcessor CreateProcessor() =>
        new(_repo.Object, _sender.Object, NullLogger<AttendanceConsolidationProcessor>.Instance);

    [Fact]
    public async Task RecalculatesTheClaimedRange_AndCompletesWithTheClaimedVersion()
    {
        _repo.Setup(r => r.CompleteAsync(_claim.Id, Consumer, 4, It.IsAny<CancellationToken>())).ReturnsAsync(ConsolidationCompletion.Completed);

        var outcome = await CreateProcessor().ProcessAsync(_claim, Consumer, _options, CancellationToken.None);

        outcome.Should().Be(ConsolidationOutcome.Completed);
        _sender.Verify(s => s.Send(It.Is<ProcessDailyAttendanceCommand>(c =>
            c.StartDate == _claim.StartDate && c.EndDate == _claim.EndDate && c.EmployeeId == _claim.EmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenTheRequestWasExpandedMeanwhile_ReportsRequeued()
    {
        _repo.Setup(r => r.CompleteAsync(_claim.Id, Consumer, 4, It.IsAny<CancellationToken>())).ReturnsAsync(ConsolidationCompletion.Requeued);

        (await CreateProcessor().ProcessAsync(_claim, Consumer, _options, CancellationToken.None)).Should().Be(ConsolidationOutcome.Requeued);
    }

    [Fact]
    public async Task RecalculationFailure_IsRecordedWithBackoff_AndNotCompleted()
    {
        _sender.Setup(s => s.Send(It.IsAny<ProcessDailyAttendanceCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("datos inválidos"));

        var outcome = await CreateProcessor().ProcessAsync(_claim, Consumer, _options, CancellationToken.None);

        outcome.Should().Be(ConsolidationOutcome.Failed);
        _repo.Verify(r => r.FailAsync(_claim.Id, Consumer, It.Is<string>(e => e.Contains("datos inválidos")), 3, TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancellation_ReleasesWithoutCountingAnAttempt()
    {
        using var cts = new CancellationTokenSource();
        _sender.Setup(s => s.Send(It.IsAny<ProcessDailyAttendanceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ProcessDailyAttendanceCommand, CancellationToken>((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => CreateProcessor().ProcessAsync(_claim, Consumer, _options, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _repo.Verify(r => r.ReleaseAsync(_claim.Id, Consumer, CancellationToken.None), Times.Once);
        _repo.Verify(r => r.FailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Maintenance_ReleasesWithoutCountingAnAttempt()
    {
        _sender.Setup(s => s.Send(It.IsAny<ProcessDailyAttendanceCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DatabaseMaintenanceException(MaintenanceState.Restoring, "admin", "restauración"));

        (await CreateProcessor().ProcessAsync(_claim, Consumer, _options, CancellationToken.None)).Should().Be(ConsolidationOutcome.Released);
        _repo.Verify(r => r.ReleaseAsync(_claim.Id, Consumer, CancellationToken.None), Times.Once);
    }

    #region F07-08: solicitudes reclamadas procesadas juntas

    private static ConsolidationClaim Claim(string employee, int version = 1, int day = 1) =>
        new(Guid.NewGuid(), EmployeeId.From(employee), new DateTime(2026, 6, day), new DateTime(2026, 6, day), version, 0);

    private void CompleteAllInBatch() =>
        _repo.Setup(r => r.CompleteManyAsync(It.IsAny<IReadOnlyCollection<(Guid RequestId, int ClaimedVersion)>>(), Consumer, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<(Guid RequestId, int ClaimedVersion)> c, string _, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, ConsolidationCompletion>)c.ToDictionary(x => x.RequestId, _ => ConsolidationCompletion.Completed));

    [Fact]
    public async Task ClaimsOfTheSameRange_AreRecalculatedInOneCommand_AndEachIsCompletedWithItsVersion()
    {
        var claims = new[] { Claim("EMP-B-1", 2), Claim("EMP-B-2", 5), Claim("EMP-B-3", 1) };
        CompleteAllInBatch();

        var outcomes = await CreateProcessor().ProcessBatchAsync(claims, Consumer, _options, CancellationToken.None);

        outcomes.Should().AllBeEquivalentTo(ConsolidationOutcome.Completed);
        _sender.Verify(s => s.Send(It.Is<ProcessDailyAttendanceCommand>(c =>
            c.EmployeeIds != null && c.EmployeeIds.Count == 3 && c.EmployeeId == null), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CompleteManyAsync(It.Is<IReadOnlyCollection<(Guid RequestId, int ClaimedVersion)>>(c =>
            c.Count == 3 && claims.All(claim => c.Contains(ValueTuple.Create(claim.Id, claim.Version)))), Consumer, It.IsAny<CancellationToken>()), Times.Once,
            "una sola confirmación para todo el grupo, cada solicitud con su versión");
    }

    [Fact]
    public async Task AFailingEmployee_OnlyCountsAnAttemptForItsOwnClaim()
    {
        var ok = Claim("EMP-B-1");
        var bad = Claim("EMP-B-2");
        _sender.Setup(s => s.Send(It.IsAny<ProcessDailyAttendanceCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AttendanceRecalculationException(2, 1,
                new[] { new AttendanceRecalculationUnitFailure("EMP-B-2", "InvalidOperationException", "datos inválidos") }, null));
        CompleteAllInBatch();

        var outcomes = await CreateProcessor().ProcessBatchAsync(new[] { ok, bad }, Consumer, _options, CancellationToken.None);

        outcomes.Should().Equal(ConsolidationOutcome.Completed, ConsolidationOutcome.Failed);
        _repo.Verify(r => r.CompleteManyAsync(It.Is<IReadOnlyCollection<(Guid RequestId, int ClaimedVersion)>>(c =>
            c.Count == 1 && c.Single().RequestId == ok.Id), Consumer, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.FailAsync(bad.Id, Consumer, It.Is<string>(e => e.Contains("datos inválidos")), 3, TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CompleteAsync(bad.Id, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnattributedError_FallsBackToOneClaimAtATime()
    {
        var claims = new[] { Claim("EMP-B-1"), Claim("EMP-B-2") };
        _sender.Setup(s => s.Send(It.Is<ProcessDailyAttendanceCommand>(c => c.EmployeeIds != null), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("conexión perdida"));
        _repo.Setup(r => r.CompleteAsync(It.IsAny<Guid>(), Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConsolidationCompletion.Completed);

        var outcomes = await CreateProcessor().ProcessBatchAsync(claims, Consumer, _options, CancellationToken.None);

        outcomes.Should().AllBeEquivalentTo(ConsolidationOutcome.Completed);
        _sender.Verify(s => s.Send(It.Is<ProcessDailyAttendanceCommand>(c => c.EmployeeId != null), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DifferentRanges_AreRecalculatedSeparately()
    {
        var claims = new[] { Claim("EMP-B-1", day: 1), Claim("EMP-B-2", day: 1), Claim("EMP-B-3", day: 2) };
        CompleteAllInBatch();
        _repo.Setup(r => r.CompleteAsync(It.IsAny<Guid>(), Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConsolidationCompletion.Completed);

        await CreateProcessor().ProcessBatchAsync(claims, Consumer, _options, CancellationToken.None);

        _sender.Verify(s => s.Send(It.Is<ProcessDailyAttendanceCommand>(c => c.EmployeeIds != null && c.EmployeeIds.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.Send(It.Is<ProcessDailyAttendanceCommand>(c => c.EmployeeId == claims[2].EmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

