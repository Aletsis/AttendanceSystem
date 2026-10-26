using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Services;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Services;

public class LunchPolicyTests
{
    private static readonly DateTime Day = new(2026, 8, 3);
    private static (string Exit, DateTime ExitCivil, string? Return, DateTime? ReturnCivil) Pair(int h1, int m1, int? h2, int? m2) =>
        ("salida", Day.AddHours(h1).AddMinutes(m1), h2.HasValue ? "regreso" : null, h2.HasValue ? Day.AddHours(h2.Value).AddMinutes(m2!.Value) : null);

    [Fact]
    public void Overlap_CountsAnAbsenceThatTouchesTheWindow()
    {
        LunchPolicy.Default.Overlaps(Day.AddHours(12), Day.AddHours(12).AddMinutes(40)).Should().BeTrue("sale 12:00 y regresa 12:40: toca la ventana");
        LunchPolicy.Default.Overlaps(Day.AddHours(15), Day.AddHours(15).AddMinutes(30)).Should().BeTrue("el fin de la ventana se incluye");
        LunchPolicy.Default.Overlaps(Day.AddHours(11), Day.AddHours(12).AddMinutes(29)).Should().BeFalse();
        LunchPolicy.Default.Overlaps(Day.AddHours(15).AddMinutes(1), Day.AddHours(16)).Should().BeFalse();
        LunchPolicy.DefaultNight.Overlaps(Day.AddHours(26), Day.AddHours(26).AddMinutes(30)).Should().BeTrue("02:00 del día siguiente, ventana 23:00–04:00");
    }

    [Fact]
    public void Classify_FirstAbsenceInTheWindowIsLunch_TheRestAreTemporaryExits()
    {
        var kinds = LunchPolicy.Default.Classify(new[]
        {
            Pair(10, 0, 10, 10),     // 10 min: doble toque
            Pair(10, 30, 11, 0),     // fuera de la ventana: salida temporal
            Pair(13, 0, 13, 20),     // primera en la ventana: comida
            Pair(13, 40, 14, 20),    // en la ventana, ya hay comida: salida temporal
            Pair(15, 30, null, null) // huérfano
        });

        kinds.Should().Equal(IntermediateKind.DoubleTap, IntermediateKind.TemporaryExit, IntermediateKind.Lunch,
            IntermediateKind.TemporaryExit, IntermediateKind.Orphan);
    }

    [Fact]
    public void Classify_WithTheWindowOff_EveryAbsenceIsATemporaryExit()
    {
        var config = SystemConfiguration.CreateDefault();
        config.SetLunchWindowEnabled(false);

        config.LunchPolicy.Classify(new[] { Pair(13, 0, 14, 0) }).Should().Equal(IntermediateKind.TemporaryExit);
        config.NightLunchPolicy.WindowEnabled.Should().BeFalse();
    }

    [Fact]
    public void ShiftOverride_ReplacesOnlyThePaidMinutes()
    {
        var policy = LunchPolicy.Default.ForShift(60);
        policy.PaidMinutes.Should().Be(60);
        policy.WindowStart.Should().Be(LunchPolicy.DefaultWindowStart);
        LunchPolicy.Default.ForShift(null).Should().Be(LunchPolicy.Default);
    }

    [Fact]
    public void Configuration_RejectsAnInvertedWindow()
    {
        var config = SystemConfiguration.CreateDefault();
        var act = () => config.UpdateLunchSettings(new TimeSpan(15, 0, 0), new TimeSpan(12, 0, 0), 30);
        act.Should().Throw<DomainException>();
        config.LunchPolicy.Should().Be(LunchPolicy.Default);
    }
}
