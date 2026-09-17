using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Primitives;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class PositionTests
{
    [Fact]
    public void Create_WithValidData_ShouldInstantiatePosition()
    {
        // Act
        var position = Position.Create("Gerente de Planta", "Dirección operativa", 50000m, isCritical: true);

        // Assert
        position.Should().NotBeNull();
        position.Name.Should().Be("Gerente de Planta");
        position.Description.Should().Be("Dirección operativa");
        position.BaseSalary.Should().Be(50000m);
        position.IsCritical.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsEmpty_ShouldThrowDomainException(string invalidName)
    {
        // Act
        var act = () => Position.Create(invalidName, "Desc", 10000m);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*nombre del puesto es requerido*");
    }

    [Fact]
    public void Update_ShouldModifyProperties()
    {
        // Arrange
        var pos = Position.Create("Analista Jr", "Junior", 15000m, false);

        // Act
        pos.Update("Analista Sr", "Senior", 28000m, true);

        // Assert
        pos.Name.Should().Be("Analista Sr");
        pos.Description.Should().Be("Senior");
        pos.BaseSalary.Should().Be(28000m);
        pos.IsCritical.Should().BeTrue();
    }
}
