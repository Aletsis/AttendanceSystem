using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class ExternalEmployeeTests
{
    private readonly BranchId _branchId = BranchId.CreateNew();

    [Fact]
    public void Create_ValidExternalEmployee_ShouldSetProperties()
    {
        // Act
        var emp = ExternalEmployee.Create(
            _branchId,
            "101",
            "Carlos",
            "Santana",
            "carlos@externa.com",
            "555-9876",
            "Cajero",
            "Operaciones",
            EmployeeStatus.Alta,
            "RFID-999");

        // Assert
        emp.Id.Should().NotBeEmpty();
        emp.BranchId.Should().Be(_branchId);
        emp.EmployeeNumber.Should().Be("101");
        emp.FirstName.Should().Be("Carlos");
        emp.LastName.Should().Be("Santana");
        emp.GetFullName().Should().Be("Carlos Santana");
        emp.Email.Should().Be("carlos@externa.com");
        emp.PhoneNumber.Should().Be("555-9876");
        emp.Position.Should().Be("Cajero");
        emp.Department.Should().Be("Operaciones");
        emp.Status.Should().Be(EmployeeStatus.Alta);
        emp.CardNumber.Should().Be("RFID-999");
        emp.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        emp.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Update_ShouldModifyPropertiesAndSetUpdatedAt()
    {
        // Arrange
        var emp = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana");

        // Act
        emp.Update(
            _branchId,
            "102",
            "Carlos Eduardo",
            "Santana Gomez",
            "carlos.gomez@externa.com",
            "555-1111",
            "Gerente",
            "Administracion",
            EmployeeStatus.Alta,
            "RFID-1000");

        // Assert
        emp.EmployeeNumber.Should().Be("102");
        emp.FirstName.Should().Be("Carlos Eduardo");
        emp.LastName.Should().Be("Santana Gomez");
        emp.Position.Should().Be("Gerente");
        emp.Department.Should().Be("Administracion");
        emp.CardNumber.Should().Be("RFID-1000");
        emp.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deactivate_And_Activate_ShouldUpdateStatus()
    {
        // Arrange
        var emp = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana");

        // Act
        emp.Deactivate();

        // Assert
        emp.Status.Should().Be(EmployeeStatus.Baja);
        emp.UpdatedAt.Should().NotBeNull();

        // Act
        emp.Activate();

        // Assert
        emp.Status.Should().Be(EmployeeStatus.Alta);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_InvalidEmployeeNumber_ShouldThrowDomainException(string? invalidNumber)
    {
        // Act
        var act = () => ExternalEmployee.Create(_branchId, invalidNumber!, "Carlos", "Santana");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*número de empleado es requerido*");
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("email@")]
    public void Create_InvalidEmailFormat_ShouldThrowDomainException(string invalidEmail)
    {
        // Act
        var act = () => ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana", email: invalidEmail);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*formato del email no es válido*");
    }
}
