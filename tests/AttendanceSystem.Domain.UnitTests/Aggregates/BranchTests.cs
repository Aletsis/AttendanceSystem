using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Primitives;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class BranchTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldInstantiateBranch()
    {
        // Act
        var branch = Branch.Create("A01", "Sucursal Central", "Av. Reforma 100", false);

        // Assert
        branch.Should().NotBeNull();
        branch.Code.Should().Be("A01");
        branch.Name.Should().Be("Sucursal Central");
        branch.Address.Should().Be("Av. Reforma 100");
        branch.IsExternal.Should().BeFalse();
        branch.ExternalHost.Should().BeNull();
    }

    [Fact]
    public void Create_WhenExternalBranchWithoutHost_ShouldThrowDomainException()
    {
        // Act
        var act = () => Branch.Create("B02", "Sucursal Remota", "Calle 2", isExternal: true, externalHost: null);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*host es requerido*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("A1")]
    [InlineData("A001")]
    [InlineData("101")]
    [InlineData("AAA")]
    public void Create_WhenCodeIsInvalidFormat_ShouldThrowDomainException(string invalidCode)
    {
        // Act
        var act = () => Branch.Create(invalidCode, "Sucursal", null);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_WithValidData_ShouldModifyProperties()
    {
        // Arrange
        var branch = Branch.Create("C01", "Sucursal Vieja", "Dir Vieja");

        // Act
        branch.Update("C02", "Sucursal Nueva", "Dir Nueva", isExternal: true, externalHost: "192.168.10.5");

        // Assert
        branch.Code.Should().Be("C02");
        branch.Name.Should().Be("Sucursal Nueva");
        branch.Address.Should().Be("Dir Nueva");
        branch.IsExternal.Should().BeTrue();
        branch.ExternalHost.Should().Be("192.168.10.5");
    }
}
