using System;
using System.Collections.Generic;
using System.Linq;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Shifts;
using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Shifts;

/// <summary>Caso 9: reglas de la plantilla del turno mixto compartidas por el servidor y las pantallas.</summary>
public class MixedShiftScheduleTests
{
    private static ShiftDto Fixed(string name, int startHour, int hours, ShiftType type = ShiftType.Matutino) =>
        new(Guid.NewGuid(), name, TimeSpan.FromHours(startHour), TimeSpan.FromHours((startHour + hours) % 24), 10,
            TimeSpan.FromHours(hours), type, Enumerable.Empty<ShiftDayDto>(), false, 0);

    private static Dictionary<DayOfWeek, ShiftDto> Week(ShiftDto all, params (DayOfWeek Day, ShiftDto Shift)[] overrides)
    {
        var days = MixedShiftSchedule.Week.ToDictionary(d => d, _ => all);
        foreach (var (day, shift) in overrides) days[day] = shift;
        return days;
    }

    [Fact]
    public void MissingDays_AreListedInWeekOrder()
    {
        MixedShiftSchedule.MissingDays(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
            .Should().Equal(DayOfWeek.Tuesday, DayOfWeek.Sunday);
    }

    [Fact]
    public void NightFollowedByAnEarlyMorning_IsWarned()
    {
        var days = Week(Fixed("Mat 9-18", 9, 9),
            (DayOfWeek.Friday, Fixed("Noc 22-06", 22, 8, ShiftType.Nocturno)),
            (DayOfWeek.Saturday, Fixed("Mat 6-14", 6, 8)));

        var warnings = MixedShiftSchedule.AdjacencyWarnings(days);

        warnings.Should().ContainSingle()
            .Which.Should().Contain("El viernes (Noc 22-06) termina a las 06:00 del sábado")
            .And.Contain("el sábado (Mat 6-14) empieza a las 06:00");
    }

    [Fact]
    public void LessThanTwoHoursBetweenDays_IsWarned_TwoOrMoreIsNot()
    {
        var night = Fixed("Noc 22-06", 22, 8, ShiftType.Nocturno);
        MixedShiftSchedule.AdjacencyWarnings(Week(Fixed("Mat 9-18", 9, 9), (DayOfWeek.Friday, night), (DayOfWeek.Saturday, Fixed("Mat 7-15", 7, 8))))
            .Should().HaveCount(1, "1 h entre las 06:00 y las 07:00");
        MixedShiftSchedule.AdjacencyWarnings(Week(Fixed("Mat 9-18", 9, 9), (DayOfWeek.Friday, night), (DayOfWeek.Saturday, Fixed("Mat 8-16", 8, 8))))
            .Should().BeEmpty("la entrada del sábado se acepta desde las 06:00");
    }

    [Fact]
    public void RegularWeek_HasNoWarnings_AndSundayToMondayIsChecked()
    {
        MixedShiftSchedule.AdjacencyWarnings(Week(Fixed("Mat 9-18", 9, 9))).Should().BeEmpty();
        MixedShiftSchedule.AdjacencyWarnings(Week(Fixed("Mat 6-14", 6, 8), (DayOfWeek.Sunday, Fixed("Noc 22-06", 22, 8, ShiftType.Nocturno))))
            .Should().ContainSingle().Which.Should().Contain("El domingo");
    }

    [Fact]
    public void FlexibleWithoutWindow_HasNoStartAndIsNotChecked()
    {
        var free = new ShiftDto(Guid.NewGuid(), "Libre", TimeSpan.Zero, TimeSpan.FromHours(8), 0, TimeSpan.FromHours(8), ShiftType.Flexible,
            Enumerable.Empty<ShiftDayDto>(), false, 0, HasEntryWindow: false);
        MixedShiftSchedule.AdjacencyWarnings(Week(free, (DayOfWeek.Friday, Fixed("Noc 22-06", 22, 8, ShiftType.Nocturno)))).Should().BeEmpty();
    }
}
