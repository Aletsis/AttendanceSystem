using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class TimeNormalizerTests
{
    private readonly TimeNormalizer _normalizer;

    public TimeNormalizerTests()
    {
        _normalizer = new TimeNormalizer(NullLogger<TimeNormalizer>.Instance);
    }

    [Fact]
    public void ToUtc_WhenAlreadyUtc_ShouldReturnSameInstantAndKind()
    {
        // Arrange
        var utc = new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _normalizer.ToUtc(utc);

        // Assert
        result.Should().Be(utc);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_WithSpecificTimeZone_ShouldConvertToCorrectUtcInstant()
    {
        // Arrange: 08:00 en zona UTC-6 (Central Standard Time (Mexico) o America/Mexico_City)
        // En octubre, México no tiene horario de verano (UTC-6 todo el año)
        var localTime = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Unspecified);
        var timeZoneId = OperatingSystem.IsWindows() ? "Central Standard Time (Mexico)" : "America/Mexico_City";

        // Act
        var utcResult = _normalizer.ToUtc(localTime, timeZoneId);

        // Assert: 08:00 UTC-6 == 14:00 UTC
        utcResult.Kind.Should().Be(DateTimeKind.Utc);
        utcResult.Hour.Should().Be(14);
        utcResult.Day.Should().Be(17);
    }

    [Fact]
    public void ToWorkDate_ShouldReflectCivilLaborDate_NotUtcDate()
    {
        // Arrange: Marcaje a las 20:00 (8:00 PM) del 17 de octubre en UTC-6
        // En UTC esto corresponde a las 02:00 AM del 18 de octubre
        var timeZoneId = OperatingSystem.IsWindows() ? "Central Standard Time (Mexico)" : "America/Mexico_City";
        var utcInstant = new DateTime(2026, 10, 18, 2, 0, 0, DateTimeKind.Utc);

        // Act
        var workDate = _normalizer.ToWorkDate(utcInstant, timeZoneId);

        // Assert: La fecha laboral civil debe ser el 17 de octubre, no el 18
        workDate.Should().Be(new DateOnly(2026, 10, 17));
    }

    [Fact]
    public void ToCivilTime_ShouldConvertFromUtcBackToLocalClock()
    {
        // Arrange
        var timeZoneId = OperatingSystem.IsWindows() ? "Central Standard Time (Mexico)" : "America/Mexico_City";
        var utcInstant = new DateTime(2026, 10, 17, 14, 30, 0, DateTimeKind.Utc);

        // Act
        var civilTime = _normalizer.ToCivilTime(utcInstant, timeZoneId);

        // Assert
        civilTime.Hour.Should().Be(8);
        civilTime.Minute.Should().Be(30);
        civilTime.Day.Should().Be(17);
    }

    [Fact]
    public void ResolveTimeZone_WithUnknownId_ShouldFallbackToTheDefaultZoneWithoutThrowing()
    {
        // Act
        var tz = _normalizer.ResolveTimeZone("NonExistent/Unknown_TimeZone_123");

        // Assert: cae a la zona autoritativa por defecto, no a la zona local del equipo (en CI la local es UTC).
        tz.Should().NotBeNull();
        tz.Should().Be(_normalizer.ResolveTimeZone(TimeNormalizer.DefaultAuthoritativeTimeZoneId));
    }

    [Fact]
    public void ResolveTimeZone_WithAZoneMissingOnTheSystem_UsesItsEquivalentRules()
    {
        // America/Ciudad_Juarez (2022) no existe en algunos Windows: se usa la de reglas iguales, no la de Ciudad de México.
        var tz = _normalizer.ResolveTimeZone("America/Ciudad_Juarez");
        var summer = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        tz.GetUtcOffset(summer).Should().Be(TimeSpan.FromHours(-6));
        tz.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(-7));
    }
}
