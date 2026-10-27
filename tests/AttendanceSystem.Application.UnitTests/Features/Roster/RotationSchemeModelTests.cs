using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Roster;

public class RotationSchemeModelTests
{
    private static readonly Guid First = Guid.NewGuid(), Second = Guid.NewGuid(), Base = Guid.NewGuid();

    [Fact]
    public void RotatingSchemes_UseTheFirstShiftAndTheirFixedRestDaysForTheEmployee()
    {
        var model = new RotationSchemeModel
        {
            SchemeType = RotationSchemeType.Rotativo2x8,
            Shift1 = First, Shift2 = Second, BaseShift = Base,
            SelectedFixedRestDays = new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday }
        };

        model.PrimaryShift.Should().Be(First);
        model.ProfileRestDays.Should().BeEquivalentTo(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
    }

    [Fact]
    public void CycleSchemes_UseTheBaseShiftAndKeepTheEmployeeRestDays()
    {
        var model = new RotationSchemeModel { SchemeType = RotationSchemeType.Esquema4x3, Shift1 = First, BaseShift = Base };

        model.PrimaryShift.Should().Be(Base);
        model.ProfileRestDays.Should().BeNull();
    }

    [Fact]
    public void RotatingScheme_WithoutFixedRestDays_KeepsTheEmployeeRestDays()
    {
        var model = new RotationSchemeModel
        {
            SchemeType = RotationSchemeType.Rotativo3x8,
            SelectedFixedRestDays = new HashSet<DayOfWeek>()
        };

        model.ProfileRestDays.Should().BeNull();
    }
}
