using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class EmployeeTests
{
    private readonly EmployeeId _id = EmployeeId.From("EMP-001");
    private readonly BranchId _branchId = BranchId.CreateNew();
    private readonly DepartmentId _departmentId = DepartmentId.CreateNew();
    private readonly PositionId _positionId = PositionId.CreateNew();

    [Fact]
    public void Create_ValidEmployee_ShouldSetPropertiesWithStatusAlta()
    {
        // Act
        var employee = Employee.Create(
            id: _id,
            firstName: "Juan",
            lastName: "Perez",
            email: "juan.perez@empresa.com",
            phoneNumber: "555-1234",
            hireDate: new DateTime(2025, 1, 15),
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino);

        // Assert
        employee.Id.Should().Be(_id);
        employee.FirstName.Should().Be("Juan");
        employee.LastName.Should().Be("Perez");
        employee.GetFullName().Should().Be("Juan Perez");
        employee.Email.Should().Be("juan.perez@empresa.com");
        employee.Status.Should().Be(EmployeeStatus.Alta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenFirstNameIsEmpty_ShouldThrowDomainException(string emptyName)
    {
        // Act
        Action act = () => Employee.Create(
            id: _id,
            firstName: emptyName,
            lastName: "Perez",
            email: "juan@empresa.com",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: null);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*no puede estar vacío*");
    }

    [Fact]
    public void Create_WhenInvalidEmailFormat_ShouldThrowDomainException()
    {
        // Act
        Action act = () => Employee.Create(
            id: _id,
            firstName: "Juan",
            lastName: "Perez",
            email: "correo-invalido-sin-arroba",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: null);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*formato del email no es válido*");
    }

    [Fact]
    public void Deactivate_ShouldChangeStatusToBaja()
    {
        // Arrange
        var employee = Employee.Create(
            id: _id,
            firstName: "Juan",
            lastName: "Perez",
            email: "juan@empresa.com",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: null);

        // Act
        employee.Deactivate();

        // Assert
        employee.Status.Should().Be(EmployeeStatus.Baja);
    }
}
