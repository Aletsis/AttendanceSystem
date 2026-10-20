using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.EventHandlers;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Events;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>
/// El evento de marcaje se convierte en una solicitud durable (operación atómica del repositorio) y en
/// un aviso al consumidor; no encola un trabajo por marcaje ni absorbe errores.
/// </summary>
public class AttendanceConsolidationTriggerTests
{
    private readonly Mock<IAttendanceConsolidationRepository> _consolidationRepoMock = new();
    private readonly Mock<IConsolidationWakeup> _wakeupMock = new();
    private readonly AttendanceRecordedEventHandler _handler;
    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-CONS-1");

    public AttendanceConsolidationTriggerTests()
    {
        _handler = new AttendanceRecordedEventHandler(
            _consolidationRepoMock.Object,
            _wakeupMock.Object,
            NullLogger<AttendanceRecordedEventHandler>.Instance);
    }

    [Fact]
    public async Task Handle_UsesTheCivilWorkDate_AndSignalsTheConsumer()
    {
        // 23:30 civil del 15 se guarda como 05:30 UTC del 16: la jornada afectada es la del 15.
        var checkTimeUtc = new DateTime(2026, 10, 16, 5, 30, 0, DateTimeKind.Utc);
        var domainEvent = new AttendanceRecordedEvent(AttendanceRecordId.CreateNew(), _employeeId, checkTimeUtc, CheckType.CheckIn, new DateOnly(2026, 10, 15));

        await _handler.Handle(domainEvent, CancellationToken.None);

        _consolidationRepoMock.Verify(r => r.RequestAsync(_employeeId, new DateTime(2026, 10, 15), new DateTime(2026, 10, 15), It.IsAny<CancellationToken>()), Times.Once);
        _wakeupMock.Verify(w => w.Signal(), Times.Once);
    }

    [Fact]
    public async Task Handle_LegacyMessageWithoutWorkDate_CoversTheUtcDayAndThePreviousOne()
    {
        var checkTime = new DateTime(2026, 10, 15, 8, 30, 0);
        var domainEvent = new AttendanceRecordedEvent(AttendanceRecordId.CreateNew(), _employeeId, checkTime, CheckType.CheckIn);

        await _handler.Handle(domainEvent, CancellationToken.None);

        _consolidationRepoMock.Verify(r => r.RequestAsync(_employeeId, checkTime.Date.AddDays(-1), checkTime.Date, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheRequestCannotBeSaved_PropagatesSoTheOutboxRetries()
    {
        _consolidationRepoMock
            .Setup(r => r.RequestAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("base de datos no disponible"));
        var domainEvent = new AttendanceRecordedEvent(AttendanceRecordId.CreateNew(), _employeeId, DateTime.UtcNow, CheckType.CheckIn, new DateOnly(2026, 10, 15));

        var act = () => _handler.Handle(domainEvent, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("un evento cuya solicitud no se guardó no debe marcarse como procesado");
        _wakeupMock.Verify(w => w.Signal(), Times.Never);
    }
}
