using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

public class OutboxAndDomainEventsTests
{
    private AttendanceDbContext CreateInMemoryContext(IPublisher publisher)
    {
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AttendanceDbContext(options, publisher);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldDispatchDomainEvents_ClearFromEntity_AndRecordInOutbox()
    {
        // Arrange
        var publisherMock = new Mock<IPublisher>();
        await using var context = CreateInMemoryContext(publisherMock.Object);

        var record = AttendanceRecord.Create(
            EmployeeId.From("EMP-001"),
            DeviceId.From("DEV-01"),
            DateTime.UtcNow,
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);

        // El aggregate root tiene eventos de dominio pendientes
        record.DomainEvents.Should().NotBeEmpty();

        context.AttendanceRecords.Add(record);

        // Act 1: Primer SaveChangesAsync
        await context.SaveChangesAsync();

        // Assert 1:
        // - El evento se despachó exactamente una vez vía MediatR
        publisherMock.Verify(p => p.Publish(It.Is<INotification>(n => n is DomainEvent), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // - H24: Los eventos se limpiaron de la entidad (evita retención en memoria)
        record.DomainEvents.Should().BeEmpty();

        // - El mensaje de Outbox fue creado y marcado como procesado
        var outboxMessage = await context.OutboxMessages.FirstOrDefaultAsync();
        outboxMessage.Should().NotBeNull();
        outboxMessage!.ProcessedOnUtc.Should().NotBeNull();
        outboxMessage.Error.Should().BeNull();

        // Act 2: Segundo SaveChangesAsync sin cambios
        var callCountBefore = publisherMock.Invocations.Count;
        await context.SaveChangesAsync();

        // Assert 2: No se debe volver a despachar ningún evento
        publisherMock.Invocations.Count.Should().Be(callCountBefore);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenPublisherFails_ShouldPersistEntityAndMarkOutboxFailed()
    {
        // Arrange
        var publisherMock = new Mock<IPublisher>();
        publisherMock.Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Servicio de notificación temporalmente no disponible"));

        await using var context = CreateInMemoryContext(publisherMock.Object);

        var record = AttendanceRecord.Create(
            EmployeeId.From("EMP-002"),
            DeviceId.From("DEV-01"),
            DateTime.UtcNow,
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);

        context.AttendanceRecords.Add(record);

        // Act: SaveChangesAsync no debe propagar la excepción hacia el agregador
        var action = async () => await context.SaveChangesAsync();
        await action.Should().NotThrowAsync();

        // Assert:
        // - El registro de asistencia quedó guardado en BD
        var savedRecord = await context.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == record.Id);
        savedRecord.Should().NotBeNull();

        // - El mensaje en Outbox queda con ProcessedOnUtc = null y el Error registrado
        var outboxMessage = await context.OutboxMessages.FirstOrDefaultAsync();
        outboxMessage.Should().NotBeNull();
        outboxMessage!.ProcessedOnUtc.Should().BeNull();
        outboxMessage.Error.Should().Contain("Servicio de notificación temporalmente no disponible");
        outboxMessage.RetryCount.Should().Be(1);
    }
}
