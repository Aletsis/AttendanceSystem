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
            "101",
            "Carlos Eduardo",
            "Santana Gomez",
            "carlos.gomez@externa.com",
            "555-1111",
            "Gerente",
            "Administracion",
            EmployeeStatus.Alta,
            "RFID-1000");

        // Assert
        emp.EmployeeNumber.Should().Be("101");
        emp.FirstName.Should().Be("Carlos Eduardo");
        emp.LastName.Should().Be("Santana Gomez");
        emp.Position.Should().Be("Gerente");
        emp.Department.Should().Be("Administracion");
        emp.CardNumber.Should().Be("RFID-1000");
        emp.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_WhenEmployeeNumberChanges_ShouldThrowDomainException()
    {
        var emp = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana");

        var act = () => emp.Update(_branchId, "102", "Carlos", "Santana", null, null, null, null, EmployeeStatus.Alta);

        act.Should().Throw<DomainException>().WithMessage("*no se puede cambiar*");
        emp.EmployeeNumber.Should().Be("101");
    }

    [Fact]
    public void Update_WhenBranchChanges_ShouldThrowDomainException()
    {
        var emp = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana");

        var act = () => emp.Update(BranchId.CreateNew(), "101", "Carlos", "Santana", null, null, null, null, EmployeeStatus.Alta);

        act.Should().Throw<DomainException>().WithMessage("*sucursal*no se puede cambiar*");
        emp.BranchId.Should().Be(_branchId);
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

    [Fact]
    public void UpdateFromDevice_ReplacesEachFingerByIndex_WithoutDuplicates()
    {
        var emp = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana");
        emp.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(0, "A"), new ExternalEmployeeFingerprint(1, "B") })
            .Should().BeTrue();

        emp.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(1, "B2") }).Should().BeTrue();

        emp.Fingerprints.Should().HaveCount(2);
        emp.Fingerprints.Should().ContainSingle(f => f.FingerIndex == 1 && f.Template == "B2");
        emp.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateFromDevice_SameTemplatesOrEmptyOnes_ChangeNothing()
    {
        var emp = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana");
        emp.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(0, "A") });

        emp.UpdateFromDevice(new[] { new ExternalEmployeeFingerprint(0, "A"), new ExternalEmployeeFingerprint(2, " ") })
            .Should().BeFalse();

        emp.Fingerprints.Should().ContainSingle();
    }

    [Fact]
    public void UpdateFromDevice_TakesTheCardOnlyWhenTheEmployeeHasNone()
    {
        var withCard = ExternalEmployee.Create(_branchId, "101", "Carlos", "Santana", cardNumber: "RFID-1");
        var withoutCard = ExternalEmployee.Create(_branchId, "102", "Ana", "López");

        withCard.UpdateFromDevice(null, "RFID-2").Should().BeFalse();
        withoutCard.UpdateFromDevice(null, " RFID-3 ").Should().BeTrue();

        withCard.CardNumber.Should().Be("RFID-1");
        withoutCard.CardNumber.Should().Be("RFID-3");
    }
}
