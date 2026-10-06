using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Roster;


public class GenerateRotationPatternCommandHandlerTests
{
    private readonly Mock<IShiftRosterRepository> _rosterRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly GenerateRotationPatternCommandHandler _handler;

    public GenerateRotationPatternCommandHandlerTests()
    {
        _rosterRepoMock = new Mock<IShiftRosterRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new GenerateRotationPatternCommandHandler(_rosterRepoMock.Object, _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task Handle_Esquema4x3_ShouldGenerate4WorkDaysAnd3RestDaysPerCycle()
    {
        // Arrange
        var employeeId = "EMP-001";
        var startDate = new DateTime(2026, 10, 5); // Day 0
        var endDate = new DateTime(2026, 10, 11);  // Day 6 (7 days total)
        var shiftId = Guid.NewGuid();

        _rosterRepoMock.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        List<ShiftRoster>? savedRosters = null;
        _rosterRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((rosters, _) => savedRosters = rosters.ToList());

        var command = new GenerateRotationPatternCommand(
            new List<string> { employeeId },
            startDate,
            endDate,
            RotationSchemeType.Esquema4x3,
            new List<Guid> { shiftId });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(7);
        savedRosters.Should().NotBeNull();
        savedRosters!.Count.Should().Be(7);

        // Days 0..3 (4 days) should be work days with shiftId
        savedRosters.Take(4).Should().OnlyContain(r => !r.IsRestDay && r.ShiftId != null && r.ShiftId.Value == shiftId);
        // Days 4..6 (3 days) should be rest days
        savedRosters.Skip(4).Take(3).Should().OnlyContain(r => r.IsRestDay && r.ShiftId == null);
    }

    [Fact]
    public async Task Handle_Esquema24x48_ShouldGenerate1WorkDayAnd2RestDaysPerCycle()
    {
        // Arrange: 6 days -> 2 full cycles of (1 work, 2 rest)
        var employeeId = "EMP-001";
        var startDate = new DateTime(2026, 10, 5);
        var endDate = new DateTime(2026, 10, 10); // 6 days
        var shift24hId = Guid.NewGuid();

        _rosterRepoMock.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        List<ShiftRoster>? savedRosters = null;
        _rosterRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((rosters, _) => savedRosters = rosters.ToList());

        var command = new GenerateRotationPatternCommand(
            new List<string> { employeeId },
            startDate,
            endDate,
            RotationSchemeType.Esquema24x48,
            new List<Guid> { shift24hId });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(6);
        savedRosters.Should().NotBeNull();
        savedRosters!.Count.Should().Be(6);

        // Day 0: Work (guardia)
        savedRosters[0].IsRestDay.Should().BeFalse();
        savedRosters[0].ShiftId!.Value.Should().Be(shift24hId);
        // Day 1 & 2: Rest (48h)
        savedRosters[1].IsRestDay.Should().BeTrue();
        savedRosters[2].IsRestDay.Should().BeTrue();
        // Day 3: Work (guardia)
        savedRosters[3].IsRestDay.Should().BeFalse();
        // Day 4 & 5: Rest (48h)
        savedRosters[4].IsRestDay.Should().BeTrue();
        savedRosters[5].IsRestDay.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenEndDateIsNull_ShouldDefaultToOneYearProjection()
    {
        // Arrange
        var employeeId = "EMP-001";
        var startDate = new DateTime(2026, 1, 1);
        var shiftId = Guid.NewGuid();

        _rosterRepoMock.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        List<ShiftRoster>? savedRosters = null;
        _rosterRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((rosters, _) => savedRosters = rosters.ToList());

        var command = new GenerateRotationPatternCommand(
            new List<string> { employeeId },
            startDate,
            EndDate: null,
            SchemeType: RotationSchemeType.Esquema4x3,
            ShiftIds: new List<Guid> { shiftId },
            IsIndefinite: true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: 1 year from 2026-01-01 to 2027-01-01 is 366 days (inclusive)
        var expectedDays = (startDate.AddYears(1) - startDate).Days + 1;
        result.Should().Be(expectedDays);
        savedRosters.Should().NotBeNull();
        savedRosters!.Count.Should().Be(expectedDays);
    }

    [Fact]
    public async Task Handle_WhenEndDateIsBeforeStartDate_ShouldThrowArgumentException()
    {
        // Arrange
        var command = new GenerateRotationPatternCommand(
            new List<string> { "EMP-001" },
            StartDate: new DateTime(2026, 10, 10),
            EndDate: new DateTime(2026, 10, 5),
            SchemeType: RotationSchemeType.Esquema4x3,
            ShiftIds: new List<Guid> { Guid.NewGuid() });

        // Act & Assert
        var act = () => _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no puede ser anterior*");
    }

    [Fact]
    public async Task Handle_Rotativo2x8_ShouldRotateBetween2ShiftsWithRestDays()
    {
        // Arrange: 12 days total, 2 shifts, 6 days per shift, 1 rest day after rotation
        // Cycle 1: Days 0..4 = Shift 1, Day 5 = Rest
        // Cycle 2: Days 6..10 = Shift 2, Day 11 = Rest
        var employeeId = "EMP-001";
        var startDate = new DateTime(2026, 10, 5);
        var endDate = new DateTime(2026, 10, 16); // 12 days
        var shift1 = Guid.NewGuid();
        var shift2 = Guid.NewGuid();

        _rosterRepoMock.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        List<ShiftRoster>? savedRosters = null;
        _rosterRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((rosters, _) => savedRosters = rosters.ToList());

        var command = new GenerateRotationPatternCommand(
            new List<string> { employeeId },
            startDate,
            endDate,
            RotationSchemeType.Rotativo2x8,
            new List<Guid> { shift1, shift2 },
            DaysPerShift: 6,
            RestDaysAfterRotation: 1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(12);
        savedRosters.Should().NotBeNull();
        savedRosters!.Count.Should().Be(12);

        // Days 0..4: Shift 1
        for (int i = 0; i < 5; i++)
        {
            savedRosters[i].IsRestDay.Should().BeFalse();
            savedRosters[i].ShiftId!.Value.Should().Be(shift1);
            savedRosters[i].Notes.Should().Be("Rotativo 2x8 (Turno 1)");
        }

        // Day 5: Rest
        savedRosters[5].IsRestDay.Should().BeTrue();
        savedRosters[5].Notes.Should().Be("Rotativo 2x8 (Descanso)");

        // Days 6..10: Shift 2
        for (int i = 6; i < 11; i++)
        {
            savedRosters[i].IsRestDay.Should().BeFalse();
            savedRosters[i].ShiftId!.Value.Should().Be(shift2);
            savedRosters[i].Notes.Should().Be("Rotativo 2x8 (Turno 2)");
        }

        // Day 11: Rest
        savedRosters[11].IsRestDay.Should().BeTrue();
        savedRosters[11].Notes.Should().Be("Rotativo 2x8 (Descanso)");
    }

    [Fact]
    public async Task Handle_Rotativo3x8_WithFixedRestDays_ShouldPlaceRestOnSpecificDaysBetweenPeriod()
    {
        // Arrange: 14 days starting on Monday (2026-10-05)
        // 2 shifts in rotation (7 days per shift): Week 1 = Shift 1, Week 2 = Shift 2
        // Fixed rest days: Wednesday and Sunday (discontinuous rest between the period)
        var employeeId = "EMP-001";
        var startDate = new DateTime(2026, 10, 5); // Monday
        var endDate = new DateTime(2026, 10, 18);   // Sunday (14 days total)
        var shift1 = Guid.NewGuid();
        var shift2 = Guid.NewGuid();

        _rosterRepoMock.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        List<ShiftRoster>? savedRosters = null;
        _rosterRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((rosters, _) => savedRosters = rosters.ToList());

        var command = new GenerateRotationPatternCommand(
            new List<string> { employeeId },
            startDate,
            endDate,
            RotationSchemeType.Rotativo3x8,
            new List<Guid> { shift1, shift2 },
            DaysPerShift: 7,
            RestDayMode: RotationRestDayMode.FixedDaysOfWeek,
            FixedRestDays: new List<DayOfWeek> { DayOfWeek.Wednesday, DayOfWeek.Sunday });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(14);
        savedRosters.Should().NotBeNull();
        savedRosters!.Count.Should().Be(14);

        // Week 1 (Days 0..6): Shift 1
        // Day 0: Monday -> Shift 1
        savedRosters[0].IsRestDay.Should().BeFalse();
        savedRosters[0].ShiftId!.Value.Should().Be(shift1);

        // Day 1: Tuesday -> Shift 1
        savedRosters[1].IsRestDay.Should().BeFalse();
        savedRosters[1].ShiftId!.Value.Should().Be(shift1);

        // Day 2: Wednesday -> REST
        savedRosters[2].IsRestDay.Should().BeTrue();
        savedRosters[2].Notes.Should().Contain("Descanso Miércoles");

        // Day 3..5: Thursday..Saturday -> Shift 1
        for (int i = 3; i <= 5; i++)
        {
            savedRosters[i].IsRestDay.Should().BeFalse();
            savedRosters[i].ShiftId!.Value.Should().Be(shift1);
        }

        // Day 6: Sunday -> REST
        savedRosters[6].IsRestDay.Should().BeTrue();
        savedRosters[6].Notes.Should().Contain("Descanso Domingo");

        // Week 2 (Days 7..13): Shift 2
        // Day 7: Monday -> Shift 2
        savedRosters[7].IsRestDay.Should().BeFalse();
        savedRosters[7].ShiftId!.Value.Should().Be(shift2);

        // Day 8: Tuesday -> Shift 2
        savedRosters[8].IsRestDay.Should().BeFalse();
        savedRosters[8].ShiftId!.Value.Should().Be(shift2);

        // Day 9: Wednesday -> REST
        savedRosters[9].IsRestDay.Should().BeTrue();
        savedRosters[9].Notes.Should().Contain("Descanso Miércoles");

        // Day 10..12: Thursday..Saturday -> Shift 2
        for (int i = 10; i <= 12; i++)
        {
            savedRosters[i].IsRestDay.Should().BeFalse();
            savedRosters[i].ShiftId!.Value.Should().Be(shift2);
        }

        // Day 13: Sunday -> REST
        savedRosters[13].IsRestDay.Should().BeTrue();
        savedRosters[13].Notes.Should().Contain("Descanso Domingo");
    }

    [Fact]
    public async Task Handle_Rotativo2x8_WithEmployeeRestDay_ShouldUseEmployeeRestDay()
    {
        // Arrange
        var employeeId = "EMP-001";
        var empIdObj = EmployeeId.From(employeeId);
        var startDate = new DateTime(2026, 10, 5); // Monday
        var endDate = new DateTime(2026, 10, 11);  // Sunday (7 days total)
        var shift1 = Guid.NewGuid();
        var shift2 = Guid.NewGuid();

        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var employee = Employee.Create(
            empIdObj,
            "Juan",
            "Perez",
            "juan@test.com",
            null,
            DateTime.Today.AddYears(-1),
            Gender.Male,
            BranchId.CreateNew(),
            DepartmentId.CreateNew(),
            PositionId.CreateNew(),
            ShiftType.Rotativo,
            scheduleId: null,
            restDay: WeekDay.Miercoles);

        employeeRepoMock.Setup(r => r.GetByIdAsync(empIdObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);

        var handler = new GenerateRotationPatternCommandHandler(_rosterRepoMock.Object, employeeRepoMock.Object, _unitOfWorkMock.Object);

        _rosterRepoMock.Setup(r => r.GetByEmployeeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        List<ShiftRoster>? savedRosters = null;
        _rosterRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ShiftRoster>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ShiftRoster>, CancellationToken>((rosters, _) => savedRosters = rosters.ToList());

        var command = new GenerateRotationPatternCommand(
            new List<string> { employeeId },
            startDate,
            endDate,
            RotationSchemeType.Rotativo2x8,
            new List<Guid> { shift1, shift2 },
            DaysPerShift: 7,
            RestDayMode: RotationRestDayMode.FromEmployeeProfile);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(7);
        savedRosters.Should().NotBeNull();
        savedRosters!.Count.Should().Be(7);

        // Wednesday (Day 2) must be Rest
        savedRosters[2].IsRestDay.Should().BeTrue();
        savedRosters[2].Notes.Should().Contain("Descanso");

        // Other days must be Shift 1
        for (int i = 0; i < 7; i++)
        {
            if (i == 2) continue;
            savedRosters[i].IsRestDay.Should().BeFalse();
            savedRosters[i].ShiftId!.Value.Should().Be(shift1);
        }
    }
}

