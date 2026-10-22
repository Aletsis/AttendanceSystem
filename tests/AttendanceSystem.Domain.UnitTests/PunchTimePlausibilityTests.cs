using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests;

public class PunchTimePlausibilityTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 10, 0, 0);

    [Theory]
    [InlineData("2026-10-22 10:04:59", false)] // unos minutos de adelanto se toleran
    [InlineData("2026-10-22 10:05:01", true)]  // adelantado
    [InlineData("2026-10-22 13:00:00", true)]
    [InlineData("2020-01-15 08:00:00", false)] // historial antiguo de un reloj que ya operaba
    [InlineData("1999-12-31 23:59:59", true)]  // reloj sin fecha
    [InlineData("2000-01-01 00:00:00", false)]
    public void Problem_DetectsImpossibleTimes(string civil, bool impossible)
    {
        PunchTimePlausibility.Problem(DateTime.Parse(civil), Now).Should().Match(p => (p != null) == impossible);
    }

    [Fact]
    public void Quarantine_RequiresANoteAndResolvesOnce()
    {
        var punch = QuarantinedPunch.Create("E1", DeviceId.From("D1"), new DateTime(1999, 1, 1), "America/Cancun", 1, 0, "ADMS", "sin fecha");

        punch.Invoking(p => p.Discard("rh", " ")).Should().Throw<DomainException>();
        punch.Status.Should().Be(QuarantineStatus.Pending, "una decisión sin motivo no cambia el marcaje");
        punch.Discard("rh", "Reloj reiniciado");
        punch.Status.Should().Be(QuarantineStatus.Discarded);
        punch.Invoking(p => p.Release(new DateTime(2026, 1, 1), AttendanceRecordId.CreateNew(), "rh", "x")).Should().Throw<DomainException>();
    }
}
