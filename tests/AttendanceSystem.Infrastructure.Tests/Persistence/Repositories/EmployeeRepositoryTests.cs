using AttendanceSystem.Domain.Aggregates.BranchAggregate;
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

    [Fact]
    public async Task GetPaginatedAsync_ShouldFilterAndPaginateCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new EmployeeRepository(context);

        var branchA = Branch.Create("B01", "Branch Alpha", "Calle 1");
        var branchB = Branch.Create("B02", "Branch Beta", "Calle 2");
        context.Branches.AddRange(branchA, branchB);

        var dept = DepartmentId.CreateNew();

        for (int i = 1; i <= 15; i++)
        {
            var emp = Employee.Create(
                id: EmployeeId.From($"EMP-{i:D3}"),
                firstName: $"Name{i}",
                lastName: $"Last{i}",
                email: $"emp{i}@empresa.com",
                phoneNumber: null,
                hireDate: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                gender: Gender.Male,
                branchId: i <= 10 ? branchA.Id : branchB.Id,
                departmentId: dept,
                positionId: PositionId.CreateNew(),
                shiftType: null);
            repository.Add(emp);
        }
        await context.SaveChangesAsync();

        // Act - Page 1 of branchA (pageSize = 5)
        var (items, total) = await repository.GetPaginatedAsync(
            pageNumber: 1,
            pageSize: 5,
            searchString: null,
            sortLabel: null,
            sortDirection: null,
            branchFilter: new[] { "Branch Alpha" });

        // Assert
        total.Should().Be(10);
        items.Should().HaveCount(5);
        items.All(x => x.BranchId == branchA.Id).Should().BeTrue();
    }

    [Theory]
    [InlineData("juan perez", "EMP-101")]
    [InlineData("PEREZ JUAN", "EMP-101")]
    [InlineData("jose", "EMP-102")]
    [InlineData("emp-101", "EMP-101")]
    [InlineData("EMP 102", "EMP-102")]
    [InlineData("angél muñoz", "EMP-102")]
    [InlineData("angel munoz", "EMP-102")]
    public async Task GetPaginatedAsync_SearchShouldIgnoreCaseAccentsAndWordOrder(string searchTerm, string expectedId)
    {
        using var context = CreateInMemoryDbContext();
        var repository = new EmployeeRepository(context);
        var branchId = BranchId.CreateNew();
        var deptId = DepartmentId.CreateNew();
        var posId = PositionId.CreateNew();

        repository.Add(Employee.Create(
            EmployeeId.From("EMP-101"),
            "JUAN CARLOS",
            "PÉREZ LÓPEZ",
            null,
            null,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male,
            branchId,
            deptId,
            posId,
            ShiftType.Matutino,
            cardNumber: "CARD-999"));

        repository.Add(Employee.Create(
            EmployeeId.From("EMP-102"),
            "José Ángel",
            "Muñoz",
            null,
            null,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Male,
            branchId,
            deptId,
            posId,
            ShiftType.Matutino));

        await context.SaveChangesAsync();

        var (items, total) = await repository.GetPaginatedAsync(0, 10, searchTerm, null, 0);

        total.Should().Be(1);
        items.Should().ContainSingle(e => e.Id.Value == expectedId);
    }

    [Fact]
    public async Task GetPaginatedAsync_ShouldNotMatchCardNumber_AndShouldSupportCaseInsensitiveNameFilter()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new EmployeeRepository(context);

        repository.Add(Employee.Create(
            EmployeeId.From("EMP-201"),
            "MARÍA",
            "GÓMEZ",
            null,
            null,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender.Female,
            BranchId.CreateNew(),
            DepartmentId.CreateNew(),
            PositionId.CreateNew(),
            ShiftType.Matutino,
            cardNumber: "CARD-777"));

        await context.SaveChangesAsync();

        var (byCard, totalCard) = await repository.GetPaginatedAsync(0, 10, "CARD-777", null, 0);
        totalCard.Should().Be(0);
        byCard.Should().BeEmpty();

        var (byNameFilter, totalName) = await repository.GetPaginatedAsync(0, 10, null, null, 0, nameFilter: "maria gomez");
        totalName.Should().Be(1);
        byNameFilter.Should().ContainSingle(e => e.Id.Value == "EMP-201");
    }
}
