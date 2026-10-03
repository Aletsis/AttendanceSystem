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

    [Fact]
    public void Create_FlexibleShift_WithDailyHoursOnly_ShouldSucceed()
    {
        // Arrange
        var startTime = new TimeSpan(7, 30, 0);
        var windowEnd = new TimeSpan(9, 30, 0);
        var workHours = new TimeSpan(8, 0, 0);

        // Act
        var shift = Shift.Create(
            name: "Horario Flexible Diario",
            startTime: startTime,
            toleranceMinutes: 10,
            workHours: workHours,
            shiftType: ShiftType.Flexible,
            lunchBreakMinutes: 30,
            roundingsEnabled: true,
            roundingInterval: 15,
            flexWindowEndTime: windowEnd);

        // Assert
        shift.ShiftType.Should().Be(ShiftType.Flexible);
        shift.StartTime.Should().Be(startTime);
        shift.FlexWindowEndTime.Should().Be(windowEnd);
        shift.WorkHours.Should().Be(workHours);
        shift.WeeklyWorkHours.Should().BeNull();
        shift.RoundingsEnabled.Should().BeTrue();
        shift.RoundingInterval.Should().Be(15);
    }

    [Fact]
    public void Create_FlexibleShift_WithWeeklyHoursOnly_ShouldSucceed()
    {
        // Arrange
        var startTime = new TimeSpan(7, 30, 0);
        var windowEnd = new TimeSpan(9, 30, 0);
        var weeklyHours = new TimeSpan(40, 0, 0);

        // Act
        var shift = Shift.Create(
            name: "Horario Flexible Semanal",
            startTime: startTime,
            toleranceMinutes: 10,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            lunchBreakMinutes: 0,
            roundingsEnabled: true,
            roundingInterval: 15,
            flexWindowEndTime: windowEnd,
            weeklyWorkHours: weeklyHours);

        // Assert
        shift.ShiftType.Should().Be(ShiftType.Flexible);
        shift.StartTime.Should().Be(startTime);
        shift.FlexWindowEndTime.Should().Be(windowEnd);
        shift.WorkHours.Should().Be(TimeSpan.Zero);
        shift.WeeklyWorkHours.Should().Be(weeklyHours);
    }

    [Fact]
    public void Create_FlexibleShift_WithBothDailyAndWeeklyHours_ShouldThrowDomainException()
    {
        // Arrange
        var startTime = new TimeSpan(8, 0, 0);

        // Act
        Action act = () => Shift.Create(
            name: "Flexible Ambiguo",
            startTime: startTime,
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            weeklyWorkHours: new TimeSpan(40, 0, 0));

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*pero no ambas*");
    }

    [Fact]
    public void Create_FlexibleShift_WithNeitherDailyNorWeeklyHours_ShouldThrowDomainException()
    {
        // Arrange
        var startTime = new TimeSpan(8, 0, 0);

        // Act
        Action act = () => Shift.Create(
            name: "Flexible Sin Horas",
            startTime: startTime,
            toleranceMinutes: 10,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            weeklyWorkHours: null);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*debe especificarse las horas objetivo diarias o las horas objetivo semanales*");
    }

    [Fact]
    public void Create_FlexibleShift_WhenWindowEndBeforeStart_ShouldThrowDomainException()
    {
        // Arrange
        var startTime = new TimeSpan(9, 0, 0);
        var windowEnd = new TimeSpan(8, 0, 0);

        // Act
        Action act = () => Shift.Create(
            name: "Flexible Invalido",
            startTime: startTime,
            toleranceMinutes: 10,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            flexWindowEndTime: windowEnd);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*no puede ser anterior a la hora de inicio*");
    }

    [Fact]
    public void Create_SplitShift_WithValidBlocks_ShouldCalculateTotalWorkHoursAndProperties()
    {
        // Arrange: Bloque 1: 09:00 - 14:00 (5h), Bloque 2: 17:00 - 21:00 (4h) -> Total 9h
        var startB1 = new TimeSpan(9, 0, 0);
        var durB1 = new TimeSpan(5, 0, 0);
        var startB2 = new TimeSpan(17, 0, 0);
        var endB2 = new TimeSpan(21, 0, 0);

        // Act
        var shift = Shift.Create(
            name: "Horario Partido Restaurante",
            startTime: startB1,
            toleranceMinutes: 10,
            workHours: durB1,
            shiftType: ShiftType.Partido,
            secondBlockStartTime: startB2,
            secondBlockEndTime: endB2,
            secondBlockToleranceMinutes: 15);

        // Assert
        shift.ShiftType.Should().Be(ShiftType.Partido);
        shift.StartTime.Should().Be(startB1);
        shift.EndTime.Should().Be(new TimeSpan(14, 0, 0));
        shift.ToleranceMinutes.Should().Be(10);
        shift.SecondBlockStartTime.Should().Be(startB2);
        shift.SecondBlockEndTime.Should().Be(endB2);
        shift.SecondBlockToleranceMinutes.Should().Be(15);
        shift.WorkHours.Should().Be(new TimeSpan(9, 0, 0));
    }

    [Fact]
    public void Create_SplitShift_OvernightSecondBlock_ShouldCalculateCrossMidnightHours()
    {
        // Arrange: Bloque 1: 10:00 - 14:00 (4h), Bloque 2: 19:00 - 02:00 (7h) -> Total 11h
        var startB1 = new TimeSpan(10, 0, 0);
        var durB1 = new TimeSpan(4, 0, 0);
        var startB2 = new TimeSpan(19, 0, 0);
        var endB2 = new TimeSpan(2, 0, 0);

        // Act
        var shift = Shift.Create(
            name: "Partido Noche",
            startTime: startB1,
            toleranceMinutes: 10,
            workHours: durB1,
            shiftType: ShiftType.Partido,
            secondBlockStartTime: startB2,
            secondBlockEndTime: endB2);

        // Assert
        shift.EndTime.Should().Be(new TimeSpan(14, 0, 0));
        shift.WorkHours.Should().Be(new TimeSpan(11, 0, 0));
        shift.SecondBlockToleranceMinutes.Should().Be(10); // Defaults to toleranceMinutes
    }

    [Fact]
    public void Create_SplitShift_WithoutSecondBlock_ShouldThrowDomainException()
    {
        // Arrange
        var startB1 = new TimeSpan(9, 0, 0);

        // Act
        Action act = () => Shift.Create(
            name: "Partido Sin Bloque 2",
            startTime: startB1,
            toleranceMinutes: 10,
            workHours: new TimeSpan(5, 0, 0),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: null,
            secondBlockEndTime: null);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*segundo bloque es requerida*");
    }

    [Fact]
    public void Create_SplitShift_WhenSecondBlockStartsBeforeFirstBlockEnds_ShouldThrowDomainException()
    {
        // Arrange: Bloque 1 termina a las 14:00, Bloque 2 intenta iniciar a las 13:00
        var startB1 = new TimeSpan(9, 0, 0);

        // Act
        Action act = () => Shift.Create(
            name: "Partido Traslape",
            startTime: startB1,
            toleranceMinutes: 10,
            workHours: new TimeSpan(5, 0, 0), // EndTime = 14:00
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(13, 0, 0),
            secondBlockEndTime: new TimeSpan(18, 0, 0));

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*segundo bloque no puede iniciar antes*");
    }
}
