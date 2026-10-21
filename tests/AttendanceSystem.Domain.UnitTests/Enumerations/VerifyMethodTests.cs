using AttendanceSystem.Domain.Enumerations;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Domain.UnitTests.Enumerations;

public class VerifyMethodTests
{
    [Theory]
    [InlineData(1, "Huella Dactilar")]
    [InlineData(2, "Tarjeta RFID")]
    [InlineData(3, "Contraseña")]
    [InlineData(15, "Reconocimiento Facial")]
    [InlineData(99, "Manual")]
    [InlineData(0, "Otro")]
    public void FromValue_KnownValues_ShouldReturnExpectedVerifyMethod(int value, string expectedName)
    {
        var method = VerifyMethod.FromValue(value);
        method.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(4)]
    [InlineData(25)]
    [InlineData(255)]
    [InlineData(-1)]
    public void FromValue_UnknownOrDeviceSpecificValues_ShouldFallbackToOther(int value)
    {
        var method = VerifyMethod.FromValue(value);
        method.Should().Be(VerifyMethod.Other);
        method.Id.Should().Be(0);
    }
}

public class CheckTypeTests
{
    [Theory]
    [InlineData(0, "Entrada")]
    [InlineData(1, "Salida")]
    [InlineData(2, "Inicio de Descanso")]
    [InlineData(3, "Fin de Descanso")]
    [InlineData(4, "Inicio Tiempo Extra")]
    [InlineData(5, "Fin Tiempo Extra")]
    public void FromValue_KnownValues_ShouldReturnExpectedCheckType(int value, string expectedName)
    {
        var type = CheckType.FromValue(value);
        type.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(-1)]
    [InlineData(99)]
    public void FromValue_UnknownValues_ShouldFallbackToCheckIn(int value)
    {
        var type = CheckType.FromValue(value);
        type.Should().Be(CheckType.CheckIn);
    }
}
