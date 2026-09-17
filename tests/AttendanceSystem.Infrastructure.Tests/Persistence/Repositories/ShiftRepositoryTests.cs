using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
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

public class ShiftRepositoryTests
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
    public async Task AddAsync_And_GetByIdAsync_ShouldPersistAndRetrieveShift()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new ShiftRepository(context);

        var shift = Shift.Create(
            name: "Matutino 8 a 16",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino,
            lunchBreakMinutes: 60);

        // Act
        await repository.AddAsync(shift);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(shift.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(shift.Id);
        retrieved.Name.Should().Be("Matutino 8 a 16");
        retrieved.StartTime.Should().Be(new TimeSpan(8, 0, 0));
        retrieved.ToleranceMinutes.Should().Be(10);
        retrieved.LunchBreakMinutes.Should().Be(60);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllConfiguredShifts()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new ShiftRepository(context);

        var shift1 = Shift.Create("Turno 1", new TimeSpan(7, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var shift2 = Shift.Create("Turno 2", new TimeSpan(15, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Vespertino);

        await repository.AddAsync(shift1);
        await repository.AddAsync(shift2);
        await context.SaveChangesAsync();

        // Act
        var allShifts = await repository.GetAllAsync();

        // Assert
        allShifts.Should().HaveCount(2);
        allShifts.Select(s => s.Name).Should().Contain(new[] { "Turno 1", "Turno 2" });
    }

    [Fact]
    public async Task Delete_ShouldRemoveShiftFromContext()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new ShiftRepository(context);

        var shift = Shift.Create("Turno Temporal", new TimeSpan(9, 0, 0), 5, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        await repository.AddAsync(shift);
        await context.SaveChangesAsync();

        // Act
        repository.Delete(shift);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(shift.Id);

        // Assert
        retrieved.Should().BeNull();
    }
}
