using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.Services;
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
        PunchTimePlausibility.Problem(DateTime.Parse(civil), Now, PunchTimePlausibility.EarliestValid, PunchQuarantinePolicy.Default.FutureTolerance)
            .Should().Match(p => (p != null) == impossible);
    }

    [Fact]
    public void Problem_UsesTheThresholdsItIsGiven()
    {
        var minDate = new DateTime(2026, 1, 1);

        PunchTimePlausibility.Problem(new DateTime(2025, 12, 31, 23, 0, 0), Now, minDate, TimeSpan.FromMinutes(5)).Should().Contain("fecha mínima");
        PunchTimePlausibility.Problem(Now.AddMinutes(20), Now, minDate, TimeSpan.FromMinutes(30)).Should().BeNull("se toleran 30 minutos de adelanto");
    }

    [Fact]
    public void MinimumDate_NeverGoesBelowTheAbsoluteFloor()
    {
        var device = Domain.Aggregates.DeviceAggregate.Device.Create("D1", "Reloj", "10.0.0.1", 4370);
        PunchTimePlausibility.MinimumDate(device, PunchQuarantinePolicy.Default).Should().Be(device.CreatedAt.Date.AddDays(-30));

        device.SetAcceptPunchesFrom(new DateTime(1990, 1, 1));
        PunchTimePlausibility.MinimumDate(device, PunchQuarantinePolicy.Default).Should().Be(PunchTimePlausibility.EarliestValid);
        PunchTimePlausibility.MinimumDate(null, PunchQuarantinePolicy.Default).Should().Be(PunchTimePlausibility.EarliestValid);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(5, true)]   // igual a la tolerancia hacia el futuro
    [InlineData(6, false)]  // mayor que R2: un reloj adelantado mandaría a cuarentena sin resincronizarse
    [InlineData(0, false)]
    public void Policy_SyncToleranceMustNotExceedTheOtherThresholds(int syncMinutes, bool valid)
    {
        (PunchQuarantinePolicy.Default with { ClockSyncToleranceMinutes = syncMinutes }).Validate().Should().Match(e => (e == null) == valid);
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
