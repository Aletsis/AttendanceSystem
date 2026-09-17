using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Primitives;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class DepartmentTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldInstantiateDepartment()
    {
        // Act
        var dept = Department.Create("Recursos Humanos", "Gestión de personal");

        // Assert
        dept.Should().NotBeNull();
        dept.Name.Should().Be("Recursos Humanos");
        dept.Description.Should().Be("Gestión de personal");
        dept.Positions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsEmpty_ShouldThrowDomainException(string invalidName)
    {
        // Act
        var act = () => Department.Create(invalidName, "Desc");

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage("*nombre del departamento es requerido*");
    }

    [Fact]
    public void ManagePositions_ShouldAddAndRemovePositions()
    {
        // Arrange
        var dept = Department.Create("Operaciones", null);
        var pos1 = Position.Create("Supervisor", "Líder", 20000m);
        var pos2 = Position.Create("Operario", "Línea", 12000m);

        // Act & Assert
        dept.AddPosition(pos1);
        dept.AddPosition(pos2);
        dept.Positions.Should().HaveCount(2);

        dept.RemovePosition(pos1);
        dept.Positions.Should().HaveCount(1);
        dept.Positions.Should().Contain(pos2);
    }
}
