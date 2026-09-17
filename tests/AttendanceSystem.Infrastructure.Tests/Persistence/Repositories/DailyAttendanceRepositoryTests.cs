using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
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

public class DailyAttendanceRepositoryTests
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
    public async Task Add_And_GetByEmployeeAndDateAsync_ShouldPersistAndRetrieveDailyAttendance()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new DailyAttendanceRepository(context);

        var employeeId = EmployeeId.From("EMP-100");
        var date = new DateTime(2026, 9, 17);
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        var dailyAttendance = DailyAttendance.Create(
            employeeId: employeeId,
            date: date,
            shift: shift,
            checkIn: date.AddHours(8),
            checkOut: date.AddHours(16),
            isRestDay: false);

        // Act
        repository.Add(dailyAttendance);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByEmployeeAndDateAsync(employeeId, date);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.EmployeeId.Should().Be(employeeId);
        retrieved.Date.Should().Be(date.Date);
        retrieved.ActualCheckIn.Should().Be(date.AddHours(8));
        retrieved.ActualCheckOut.Should().Be(date.AddHours(16));
        retrieved.IsAbsent.Should().BeFalse();
    }

    [Fact]
    public async Task GetByEmployeeAndDateRangeAsync_ShouldReturnOnlyRecordsWithinRange()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new DailyAttendanceRepository(context);

        var employeeId = EmployeeId.From("EMP-200");
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        var da1 = DailyAttendance.Create(employeeId, new DateTime(2026, 9, 1), shift, null, null, false);
        var da2 = DailyAttendance.Create(employeeId, new DateTime(2026, 9, 2), shift, null, null, false);
        var da3 = DailyAttendance.Create(employeeId, new DateTime(2026, 9, 10), shift, null, null, false);

        repository.Add(da1);
        repository.Add(da2);
        repository.Add(da3);
        await context.SaveChangesAsync();

        // Act
        var results = await repository.GetByEmployeeAndDateRangeAsync(
            employeeId,
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 5));

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Date).Should().ContainInOrder(new DateTime(2026, 9, 1), new DateTime(2026, 9, 2));
    }

    [Fact]
    public async Task GetByDateRangeAsync_WithBranchFilter_ShouldReturnOnlyEmployeesFromBranch()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var repository = new DailyAttendanceRepository(context);

        var branchA = Branch.Create("A01", "Sucursal A", null);
        var branchB = Branch.Create("B01", "Sucursal B", null);
        var department = Department.Create("Depto", null);
        var position = Position.Create("Puesto", null, 1000m);

        var empA = Employee.Create(EmployeeId.From("EMP-A"), "Ana", "G", null, null, DateTime.UtcNow, Gender.Female, branchA.Id, department.Id, position.Id, null);
        var empB = Employee.Create(EmployeeId.From("EMP-B"), "Bob", "H", null, null, DateTime.UtcNow, Gender.Male, branchB.Id, department.Id, position.Id, null);

        context.Branches.AddRange(branchA, branchB);
        context.Departments.Add(department);
        context.Positions.Add(position);
        context.Employees.AddRange(empA, empB);

        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var date = new DateTime(2026, 9, 15);

        var daA = DailyAttendance.Create(empA.Id, date, shift, null, null, false);
        var daB = DailyAttendance.Create(empB.Id, date, shift, null, null, false);

        repository.Add(daA);
        repository.Add(daB);
        await context.SaveChangesAsync();

        // Act
        var resultsBranchA = await repository.GetByDateRangeAsync(
            startDate: date,
            endDate: date,
            branchId: branchA.Id);

        // Assert
        resultsBranchA.Should().HaveCount(1);
        resultsBranchA.First().EmployeeId.Should().Be(empA.Id);
    }
}
