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

    [Fact]
    public void Create_WithMultipleRestDays_ShouldSetRestDaysAndPrimaryRestDay()
    {
        // Act
        var employee = Employee.Create(
            id: _id,
            firstName: "Carlos",
            lastName: "Gomez",
            email: "carlos@empresa.com",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            restDays: new[] { WeekDay.Sabado, WeekDay.Domingo });

        // Assert
        employee.RestDays.Should().BeEquivalentTo(new[] { WeekDay.Sabado, WeekDay.Domingo });
        employee.RestDay.Should().Be(WeekDay.Sabado);
        employee.IsRestDay(DayOfWeek.Saturday).Should().BeTrue();
        employee.IsRestDay(DayOfWeek.Sunday).Should().BeTrue();
        employee.IsRestDay(DayOfWeek.Monday).Should().BeFalse();
        employee.GetEffectiveRestDays().Should().BeEquivalentTo(new[] { WeekDay.Domingo, WeekDay.Sabado });
    }

    [Fact]
    public void Create_WithSingleLegacyRestDay_ShouldPopulateBothRestDayAndEffectiveRestDays()
    {
        // Act
        var employee = Employee.Create(
            id: _id,
            firstName: "Maria",
            lastName: "Lopez",
            email: "maria@empresa.com",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Female,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Vespertino,
            restDay: WeekDay.Domingo);

        // Assert
        employee.RestDay.Should().Be(WeekDay.Domingo);
        employee.IsRestDay(DayOfWeek.Sunday).Should().BeTrue();
        employee.IsRestDay(DayOfWeek.Saturday).Should().BeFalse();
        employee.GetEffectiveRestDays().Should().ContainSingle().Which.Should().Be(WeekDay.Domingo);
    }

    [Fact]
    public void Update_WithMultipleRestDays_ShouldUpdateRestDaysAndPrimaryRestDay()
    {
        // Arrange
        var employee = Employee.Create(
            id: _id,
            firstName: "Carlos",
            lastName: "Gomez",
            email: "carlos@empresa.com",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            restDay: WeekDay.Domingo);

        // Act
        employee.Update(
            firstName: "Carlos",
            lastName: "Gomez",
            email: "carlos@empresa.com",
            phoneNumber: null,
            hireDate: DateTime.Today,
            gender: Gender.Male,
            status: EmployeeStatus.Alta,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            restDays: new[] { WeekDay.Viernes, WeekDay.Sabado });

        // Assert
        employee.RestDays.Should().BeEquivalentTo(new[] { WeekDay.Viernes, WeekDay.Sabado });
        employee.RestDay.Should().Be(WeekDay.Viernes);
        employee.IsRestDay(DayOfWeek.Friday).Should().BeTrue();
        employee.IsRestDay(DayOfWeek.Saturday).Should().BeTrue();
        employee.IsRestDay(DayOfWeek.Sunday).Should().BeFalse();
    }
}
