using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence.Repositories;

public class AttendanceRepositoryTests
{
    private AttendanceDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var publisherMock = new Mock<IPublisher>();
        return new AttendanceDbContext(options, publisherMock.Object);
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_ShouldPersistAndRetrieveRecord()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new AttendanceRepository(context);

        var employeeId = EmployeeId.From("EMP-001");
        var deviceId = DeviceId.From("DEV-01");
        var checkTime = new DateTime(2026, 9, 17, 8, 30, 0, DateTimeKind.Utc);

        var record = AttendanceRecord.Create(employeeId, deviceId, checkTime, VerifyMethod.Fingerprint, CheckType.CheckIn);

        // Act
        await repository.AddAsync(record);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(record.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(record.Id);
        retrieved.EmployeeId.Should().Be(employeeId);
        retrieved.DeviceId.Should().Be(deviceId);
        retrieved.CheckTime.Should().Be(checkTime);
        retrieved.CheckType.Should().Be(CheckType.CheckIn);
    }

    [Fact]
    public async Task GetByDateRangeAsync_ShouldFilterByDateAndEmployee()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new AttendanceRepository(context);

        var emp1 = EmployeeId.From("EMP-001");
        var emp2 = EmployeeId.From("EMP-002");
        var dev = DeviceId.From("DEV-01");

        var rec1 = AttendanceRecord.Create(emp1, dev, new DateTime(2026, 9, 10, 8, 0, 0), VerifyMethod.Password, CheckType.CheckIn);
        var rec2 = AttendanceRecord.Create(emp1, dev, new DateTime(2026, 9, 15, 8, 0, 0), VerifyMethod.Password, CheckType.CheckIn);
        var rec3 = AttendanceRecord.Create(emp2, dev, new DateTime(2026, 9, 15, 8, 0, 0), VerifyMethod.Password, CheckType.CheckIn);
        var rec4 = AttendanceRecord.Create(emp1, dev, new DateTime(2026, 9, 25, 8, 0, 0), VerifyMethod.Password, CheckType.CheckIn);

        await repository.AddRangeAsync(new[] { rec1, rec2, rec3, rec4 });
        await context.SaveChangesAsync();

        // Act
        var resultEmp1 = await repository.GetByDateRangeAsync(
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 9, 20),
            emp1);

        // Assert
        resultEmp1.Should().HaveCount(1);
        resultEmp1.First().Id.Should().Be(rec2.Id);
    }

    [Fact]
    public async Task HasCheckInForDateAsync_ShouldReturnTrueWhenCheckInExists()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new AttendanceRepository(context);

        var emp = EmployeeId.From("EMP-001");
        var dev = DeviceId.From("DEV-01");
        var date = new DateTime(2026, 9, 17, 8, 15, 0, DateTimeKind.Utc);

        var rec = AttendanceRecord.Create(emp, dev, date, VerifyMethod.Fingerprint, CheckType.CheckIn);
        await repository.AddAsync(rec);
        await context.SaveChangesAsync();

        // Act
        var hasCheckIn = await repository.HasCheckInForDateAsync(emp, new DateTime(2026, 9, 17));
        var hasCheckInOtherDate = await repository.HasCheckInForDateAsync(emp, new DateTime(2026, 9, 18));

        // Assert
        hasCheckIn.Should().BeTrue();
        hasCheckInOtherDate.Should().BeFalse();
    }
}
