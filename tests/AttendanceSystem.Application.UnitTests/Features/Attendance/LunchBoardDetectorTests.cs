using System;
using System.Linq;
using AttendanceSystem.Application.Features.Attendance.Queries.GetLunchBoard;
using AttendanceSystem.Domain.Services;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class LunchBoardDetectorTests
{
    private static readonly DateTime Day = new(2026, 11, 9);
    private static readonly LunchPolicy Policy = LunchPolicy.Default; // 12:30–15:00, 30 min con goce, toque doble 15 min

    private static DateTime At(int hour, int minute = 0, int dayOffset = 0) => Day.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);

    private static LunchBoardDetector.Punch? Detect(DateTime now, DateTime? shiftEnd, LunchPolicy policy, params DateTime[] punches)
    {
        var jornada = LunchBoardDetector.CurrentJornada(punches.Select(p => new LunchBoardDetector.Punch(p)), now, policy.DoubleTapMinutes);
        return LunchBoardDetector.Detect(jornada, now, policy, shiftEnd);
    }

    [Fact]
    public void Exit_inside_window_without_return_is_lunch()
    {
        var lunch = Detect(At(13, 20), At(18), Policy, At(8, 55), At(13, 5));
        lunch!.Civil.Should().Be(At(13, 5));
    }

    [Fact]
    public void Return_removes_the_card()
    {
        Detect(At(13, 50), At(18), Policy, At(8, 55), At(13, 5), At(13, 40)).Should().BeNull();
    }

    [Fact]
    public void Exit_shortly_before_the_window_counts_when_a_normal_lunch_would_reach_it()
    {
        Detect(At(12, 15), At(18), Policy, At(8), At(12, 10)).Should().NotBeNull();
    }

    [Fact]
    public void Morning_errand_is_not_lunch()
    {
        Detect(At(10, 20), At(18), Policy, At(8), At(10)).Should().BeNull();
    }

    [Fact]
    public void Exit_at_end_of_shift_is_not_lunch()
    {
        // Turno 07:00–15:00: la salida de las 14:58 cae en la ventana pero es su salida.
        Detect(At(15, 10), At(15), Policy, At(6, 55), At(14, 58)).Should().BeNull();
    }

    [Fact]
    public void Double_tap_on_return_does_not_reopen_the_lunch()
    {
        Detect(At(14), At(18), Policy, At(9), At(13), At(13, 35), At(13, 37)).Should().BeNull();
    }

    [Fact]
    public void Double_tap_on_exit_keeps_the_first_time()
    {
        var lunch = Detect(At(13, 30), At(18), Policy, At(9), At(13), At(13, 2));
        lunch!.Civil.Should().Be(At(13));
    }

    [Fact]
    public void Second_absence_after_lunch_is_not_shown()
    {
        Detect(At(14, 40), At(18), Policy, At(9), At(13), At(13, 30), At(14, 30)).Should().BeNull();
    }

    [Fact]
    public void Lunch_after_a_morning_errand_is_still_shown()
    {
        Detect(At(13, 15), At(18), Policy, At(9), At(10), At(10, 30), At(13)).Should().NotBeNull();
    }

    [Fact]
    public void Absence_longer_than_the_cap_is_not_lunch()
    {
        Detect(At(17, 30), null, Policy, At(9), At(13)).Should().BeNull();
    }

    [Fact]
    public void Yesterdays_punches_do_not_belong_to_todays_jornada()
    {
        // Ayer: entrada y salida; hoy: sólo entrada. Está trabajando.
        Detect(At(13), At(18), Policy, At(9, 0, -1), At(18, 0, -1), At(9)).Should().BeNull();
    }

    [Fact]
    public void Night_shift_lunch_across_midnight()
    {
        var lunch = Detect(At(0, 20, 1), At(6, 0, 1), LunchPolicy.DefaultNight, At(21, 55), At(0, 5, 1));
        lunch.Should().NotBeNull();
    }

    [Fact]
    public void With_window_disabled_first_absence_is_lunch()
    {
        var policy = Policy with { WindowEnabled = false };
        Detect(At(11, 10), At(18), policy, At(9), At(11)).Should().NotBeNull();
    }

    [Fact]
    public void Explicit_break_start_is_lunch_outside_window()
    {
        var punches = new[] { new LunchBoardDetector.Punch(At(9)), new LunchBoardDetector.Punch(At(11), IsBreakStart: true) };
        var jornada = LunchBoardDetector.CurrentJornada(punches, At(11, 10), Policy.DoubleTapMinutes);
        LunchBoardDetector.Detect(jornada, At(11, 10), Policy, At(18)).Should().NotBeNull();
    }

    [Fact]
    public void Night_shift_end_crosses_midnight()
    {
        LunchBoardDetector.ShiftEnd(TimeSpan.FromHours(22), TimeSpan.FromHours(6), At(21, 50)).Should().Be(At(6, 0, 1));
        LunchBoardDetector.ShiftEnd(TimeSpan.FromHours(22), TimeSpan.FromHours(6), At(0, 10, 1)).Should().Be(At(6, 0, 1));
    }
}
