using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class ShiftTests
{
    [Fact]
    public void Create_ValidShift_ShouldSetPropertiesAndCalculateEndTime()
    {
        // Arrange
        var startTime = new TimeSpan(9, 0, 0);
        var workHours = new TimeSpan(8, 0, 0);

        // Act
        var shift = Shift.Create(
            name: "Horario Oficina",
            startTime: startTime,
            toleranceMinutes: 15,
            workHours: workHours,
            shiftType: ShiftType.Matutino,
            lunchBreakMinutes: 60);

        // Assert
        shift.Name.Should().Be("Horario Oficina");
        shift.StartTime.Should().Be(startTime);
        shift.ToleranceMinutes.Should().Be(15);
        shift.WorkHours.Should().Be(workHours);
        shift.EndTime.Should().Be(new TimeSpan(17, 0, 0));
        shift.LunchBreakMinutes.Should().Be(60);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WhenNameIsNullOrEmpty_ShouldThrowDomainException(string? invalidName)
    {
        // Act
        Action act = () => Shift.Create(
            name: invalidName!,
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*El nombre del turno es requerido*");
    }

    [Fact]
    public void Create_WhenNegativeTolerance_ShouldThrowDomainException()
    {
        // Act
        Action act = () => Shift.Create(
            name: "Turno Inválido",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: -5,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*no puede ser negativo*");
    }

    [Fact]
    public void Update_ShouldModifyPropertiesCorrectly()
    {
        // Arrange
        var shift = Shift.Create(
            name: "Turno Original",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Matutino);

        // Act
        shift.Update(
            name: "Turno Modificado",
            startTime: new TimeSpan(7, 0, 0),
            toleranceMinutes: 20,
            workHours: new TimeSpan(9, 0, 0),
            shiftType: ShiftType.Vespertino);

        // Assert
        shift.Name.Should().Be("Turno Modificado");
        shift.StartTime.Should().Be(new TimeSpan(7, 0, 0));
        shift.ToleranceMinutes.Should().Be(20);
        shift.WorkHours.Should().Be(new TimeSpan(9, 0, 0));
        shift.EndTime.Should().Be(new TimeSpan(16, 0, 0));
        shift.ShiftType.Should().Be(ShiftType.Vespertino);
    }
}
