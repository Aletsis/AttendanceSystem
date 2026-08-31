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

public class AttendanceDbContextTests
{
    private readonly Mock<IPublisher> _publisherMock = new();

    private AttendanceDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AttendanceDbContext(options, _publisherMock.Object);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAggregateHasDomainEvents_ShouldPublishEventsViaPublisher()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var employeeId = EmployeeId.From("EMP-100");
        var deviceId = DeviceId.From("DEV-01");
        var checkTime = new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc);

        var record = AttendanceRecord.Create(
            employeeId,
            deviceId,
            checkTime,
            VerifyMethod.Fingerprint,
            CheckType.CheckIn);

        context.AttendanceRecords.Add(record);

        // Act
        var result = await context.SaveChangesAsync();

        // Assert
        result.Should().BeGreaterThan(0);
        _publisherMock.Verify(p => p.Publish(It.IsAny<DomainEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
