using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Reports;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Reports;

public class AttendanceCardObservationsTests
{
    private static readonly DateTime Monday = new(2026, 10, 19);

    private static TemporaryExitDto Exit(TemporaryExitStatus status, bool lunch = false) =>
        new(Guid.NewGuid(), Monday.AddHours(10), Monday.AddHours(10.5), 30, status, lunch);

    [Fact]
    public void For_ListsMissingPunches_EarlyDeparture_AndTemporaryExits()
    {
        var day = new AttendanceReportViewDto
        {
            Date = Monday,
            MissingCheckIn = true,
            MissingCheckOut = true,
            EarlyDepartureMinutes = 25,
            TemporaryExits = new[] { Exit(TemporaryExitStatus.ApprovedPaid) }
        };

        AttendanceCardObservations.For(day, CardObservationKinds.All).Should().Equal(
            "Sin entrada",
            "Sin salida",
            "Salida anticipada de 25 min",
            "Salida temporal 10:00–10:30 (30 min) — permiso con goce");
    }

    [Fact]
    public void For_SkipsLunchesAndDismissedExits()
    {
        var day = new AttendanceReportViewDto
        {
            Date = Monday,
            TemporaryExits = new[] { Exit(TemporaryExitStatus.Lunch, lunch: true), Exit(TemporaryExitStatus.Dismissed) }
        };

        AttendanceCardObservations.For(day, CardObservationKinds.All).Should().BeEmpty();
    }

    [Fact]
    public void For_Absence_IsNotRepeatedAsMissingPunches()
    {
        var day = new AttendanceReportViewDto { Date = Monday, IsAbsent = true, MissingCheckIn = true, MissingCheckOut = true };

        AttendanceCardObservations.For(day, CardObservationKinds.All).Should().Equal("Falta");
        AttendanceCardObservations.For(day, CardObservationKinds.All & ~CardObservationKinds.Absence).Should().BeEmpty();
    }

    [Fact]
    public void For_SplitShift_NamesTheBlock()
    {
        var day = new AttendanceReportViewDto
        {
            Date = Monday,
            ShiftType = ShiftType.Partido,
            MissingBlock1CheckOut = true,
            MissingBlock2CheckIn = true
        };

        AttendanceCardObservations.For(day, CardObservationKinds.All).Should().Equal("Sin entrada (bloque 2)", "Sin salida (bloque 1)");
    }

    [Fact]
    public void For_OnlyTheSelectedKinds()
    {
        var day = new AttendanceReportViewDto { Date = Monday, MissingCheckOut = true, EarlyDepartureMinutes = 10 };

        AttendanceCardObservations.For(day, CardObservationKinds.EarlyDeparture).Should().Equal("Salida anticipada de 10 min");
        AttendanceCardObservations.For(day, CardObservationKinds.None).Should().BeEmpty();
    }

    [Fact]
    public void For_LegacyDayWithoutExitDetail_UsesTheSummary()
    {
        var day = new AttendanceReportViewDto
        {
            Date = Monday,
            HasTemporaryExits = true,
            TemporaryExitMinutes = 45,
            TemporaryExitStatus = (int)TemporaryExitStatus.Pending
        };

        AttendanceCardObservations.For(day, CardObservationKinds.All).Should().Equal("Salida temporal de 45 min — pendiente de clasificar");
    }

    [Fact]
    public void Lines_OneLinePerDayWithObservations_InSpanish()
    {
        var days = new[]
        {
            new AttendanceReportViewDto { Date = Monday.AddDays(1), IsAbsent = true },
            new AttendanceReportViewDto { Date = Monday },
            new AttendanceReportViewDto { Date = Monday.AddDays(2), MissingCheckIn = true, EarlyDepartureMinutes = 5 }
        };

        AttendanceCardObservations.Lines(days, CardObservationKinds.All).Should().Equal(
            "mar 20/10: Falta",
            "mié 21/10: Sin entrada · Salida anticipada de 5 min");
    }
}
