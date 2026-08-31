using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
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

public class EmployeeRepositoryTests
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
    public async Task Add_And_GetByIdAsync_ShouldPersistAndRetrieveEmployee()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new EmployeeRepository(context);

        var employeeId = EmployeeId.From("EMP-001");
        var branchId = BranchId.CreateNew();
        var departmentId = DepartmentId.CreateNew();
        var positionId = PositionId.CreateNew();

        var employee = Employee.Create(
            id: employeeId,
            firstName: "Maria",
            lastName: "Lopez",
            email: "maria@empresa.com",
            phoneNumber: "555-0000",
            hireDate: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            gender: Gender.Female,
            branchId: branchId,
            departmentId: departmentId,
            positionId: positionId,
            shiftType: ShiftType.Matutino);

        // Act
        repository.Add(employee);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(employeeId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(employeeId);
        retrieved.FirstName.Should().Be("Maria");
        retrieved.LastName.Should().Be("Lopez");
        retrieved.Email.Should().Be("maria@empresa.com");
    }

    [Fact]
    public async Task ExistsAsync_WhenEmployeeExists_ShouldReturnTrue()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new EmployeeRepository(context);

        var employeeId = EmployeeId.From("EMP-002");
        var employee = Employee.Create(
            id: employeeId,
            firstName: "Pedro",
            lastName: "Sanchez",
            email: "pedro@empresa.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: null);

        repository.Add(employee);
        await context.SaveChangesAsync();

        // Act
        var exists = await repository.ExistsAsync(employeeId);

        // Assert
        exists.Should().BeTrue();
    }
}
