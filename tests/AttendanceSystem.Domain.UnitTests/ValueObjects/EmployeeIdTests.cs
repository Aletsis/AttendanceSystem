using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.ValueObjects;

public class EmployeeIdTests
{
    [Fact]
    public void From_ValidValue_ShouldCreateInstance()
    {
        // Arrange
        var value = "EMP-001";

        // Act
        var employeeId = EmployeeId.From(value);

        // Assert
        employeeId.Should().NotBeNull();
        employeeId.Value.Should().Be("EMP-001");
        employeeId.ToString().Should().Be("EMP-001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void From_EmptyOrNullValue_ShouldThrowDomainException(string? invalidValue)
    {
        // Act
        Action act = () => EmployeeId.From(invalidValue!);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*EmployeeId no puede estar vacío*");
    }

    [Fact]
    public void From_ExceedsMaxLength_ShouldThrowDomainException()
    {
        // Arrange
        var tooLong = new string('A', 25);

        // Act
        Action act = () => EmployeeId.From(tooLong);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*EmployeeId no puede exceder 20 caracteres*");
    }

    [Fact]
    public void Equality_SameValues_ShouldBeEqual()
    {
        // Arrange
        var id1 = EmployeeId.From("EMP-100");
        var id2 = EmployeeId.From("EMP-100");

        // Assert
        id1.Should().Be(id2);
        (id1 == id2).Should().BeTrue();
    }
}
