using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Services;

public class ShiftDetectionTests
{
    private readonly Shift _morningShift = Shift.Create(
        name: "Turno Mañana (06:00 - 14:00)",
        startTime: new TimeSpan(6, 0, 0),
        toleranceMinutes: 10,
        workHours: new TimeSpan(8, 0, 0),
        shiftType: ShiftType.Matutino);

    private readonly Shift _afternoonShift = Shift.Create(
        name: "Turno Tarde (14:00 - 22:00)",
        startTime: new TimeSpan(14, 0, 0),
        toleranceMinutes: 10,
        workHours: new TimeSpan(8, 0, 0),
        shiftType: ShiftType.Vespertino);

    private readonly Shift _nightShift = Shift.Create(
        name: "Turno Noche (22:00 - 06:00)",
        startTime: new TimeSpan(22, 0, 0),
        toleranceMinutes: 10,
        workHours: new TimeSpan(8, 0, 0),
        shiftType: ShiftType.Nocturno);

    [Fact]
    public void FindClosestShift_WhenMorningPunch_ShouldPickMorningShift()
    {
        // Arrange: Punch at 06:05 (5 minutes after 06:00 start)
        var punchTime = new DateTime(2026, 10, 5, 6, 5, 0);
        var candidates = new[] { _morningShift, _afternoonShift, _nightShift };

        // Act
        var result = ShiftDetectionService.FindClosestShift(punchTime, candidates);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(_morningShift.Id);
    }

    [Fact]
    public void FindClosestShift_WhenAfternoonPunch_ShouldPickAfternoonShift()
    {
        // Arrange: Punch at 13:50 (10 minutes before 14:00 start)
        var punchTime = new DateTime(2026, 10, 5, 13, 50, 0);
        var candidates = new[] { _morningShift, _afternoonShift, _nightShift };

        // Act
        var result = ShiftDetectionService.FindClosestShift(punchTime, candidates);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(_afternoonShift.Id);
    }

    [Fact]
    public void FindClosestShift_WhenNightPunchAroundMidnight_ShouldHandleCircularDistanceAccurately()
    {
        // Arrange: Punch at 22:15 (15 minutes after 22:00 start)
        var punchTime = new DateTime(2026, 10, 5, 22, 15, 0);
        var candidates = new[] { _morningShift, _afternoonShift, _nightShift };

        // Act
        var result = ShiftDetectionService.FindClosestShift(punchTime, candidates);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(_nightShift.Id);
    }

    [Fact]
    public void FindClosestShift_WhenPunchExceedsMaxProximity_ShouldReturnNull()
    {
        // Arrange: Punch at 10:00 (4 hours after 06:00, 4 hours before 14:00 -> 240 min)
        // With maxProximity = 60 min
        var punchTime = new DateTime(2026, 10, 5, 10, 0, 0);
        var candidates = new[] { _morningShift, _afternoonShift, _nightShift };

        // Act
        var result = ShiftDetectionService.FindClosestShift(punchTime, candidates, maxProximityMinutes: 60);

        // Assert
        result.Should().BeNull();
    }
}
