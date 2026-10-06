using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
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
}
