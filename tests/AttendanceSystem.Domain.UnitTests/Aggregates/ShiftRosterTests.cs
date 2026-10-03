using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Aggregates;

public class ShiftRosterTests
{
    private readonly EmployeeId _employeeId = EmployeeId.From("EMP-001");
    private readonly ShiftId _shiftId = ShiftId.CreateNew();
    private readonly DateTime _date = new(2026, 10, 5);

    [Fact]
    public void Create_WithValidShift_ShouldInitializeCorrectly()
    {
        // Act
        var roster = ShiftRoster.Create(
            _employeeId,
            _date,
            _shiftId,
            isRestDay: false,
            notes: "Turno matutino");

        // Assert
        roster.EmployeeId.Should().Be(_employeeId);
        roster.Date.Should().Be(_date.Date);
        roster.ShiftId.Should().Be(_shiftId);
        roster.IsRestDay.Should().BeFalse();
        roster.Notes.Should().Be("Turno matutino");
        roster.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_AsRestDayWithoutShift_ShouldInitializeCorrectly()
    {
        // Act
        var roster = ShiftRoster.Create(
            _employeeId,
            _date,
            shiftId: null,
            isRestDay: true,
            notes: "Descanso rotativo");

        // Assert
        roster.ShiftId.Should().BeNull();
        roster.IsRestDay.Should().BeTrue();
        roster.Notes.Should().Be("Descanso rotativo");
    }

    [Fact]
    public void Create_WithoutShiftNorRestDay_ShouldThrowDomainException()
    {
        // Act
        Action act = () => ShiftRoster.Create(
            _employeeId,
            _date,
            shiftId: null,
            isRestDay: false);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*debe tener un turno asignado o estar marcado como día de descanso*");
    }

    [Fact]
    public void Update_ShouldModifyPropertiesAndSetUpdatedAt()
    {
        // Arrange
        var roster = ShiftRoster.Create(_employeeId, _date, _shiftId, isRestDay: false);
        var newShiftId = ShiftId.CreateNew();

        // Act
        roster.Update(newShiftId, isRestDay: false, notes: "Turno modificado");

        // Assert
        roster.ShiftId.Should().Be(newShiftId);
        roster.Notes.Should().Be("Turno modificado");
        roster.UpdatedAt.Should().NotBeNull();
    }
}
