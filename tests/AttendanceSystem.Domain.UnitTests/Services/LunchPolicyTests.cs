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
    public void Window_IncludesBothBoundaries()
    {
        LunchPolicy.Default.InWindow(Day.AddHours(12).AddMinutes(30)).Should().BeTrue();
        LunchPolicy.Default.InWindow(Day.AddHours(15)).Should().BeTrue();
        LunchPolicy.Default.InWindow(Day.AddHours(12).AddMinutes(29)).Should().BeFalse();
        LunchPolicy.Default.InWindow(Day.AddHours(15).AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Classify_DoubleTapsOrphansLunchAndTemporaryExits()
    {
        var result = LunchPolicy.Default.Classify(new[]
        {
            Pair(10, 0, 10, 10),   // 10 min: doble toque
            Pair(13, 0, 13, 35),   // comida
            Pair(14, 30, 14, 50),  // en ventana pero ya hay comida más cercana: salida temporal
            Pair(15, 30, null, null) // huérfano
        });

        result.Kinds.Should().Equal(IntermediateKind.DoubleTap, IntermediateKind.Lunch, IntermediateKind.TemporaryExit, IntermediateKind.Orphan);
        result.LunchMinutes.Should().Be(35);
        result.LunchDeductibleMinutes.Should().Be(0, "el descuento está desactivado por defecto");
        result.TemporaryExitMinutes.Should().Be(20);
        result.HasTemporaryExits.Should().BeTrue();
    }

    [Fact]
    public void Classify_TieOnDistance_PicksTheFirst()
    {
        var result = LunchPolicy.Default.Classify(new[] { Pair(13, 0, 13, 20), Pair(14, 0, 14, 40) });
        result.Kinds[0].Should().Be(IntermediateKind.Lunch);
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
    [Fact]
    public void Classify_WithDeductionEnabled_DeductsTheWholeLunch()
    {
        var config = SystemConfiguration.CreateDefault();
        config.SetLunchDeduction(true);

        var result = config.LunchPolicy.Classify(new[] { Pair(13, 0, 14, 30) });

        result.Kinds[0].Should().Be(IntermediateKind.Lunch);
        result.LunchMinutes.Should().Be(90);
        result.LunchDeductibleMinutes.Should().Be(90);
        config.NightLunchPolicy.DeductLunch.Should().BeTrue();
    }
}
