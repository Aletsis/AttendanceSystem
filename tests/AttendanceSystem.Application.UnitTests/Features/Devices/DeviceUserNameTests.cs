using AttendanceSystem.Application.Features.Devices;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices;

public class DeviceUserNameTests
{
    [Theory]
    [InlineData("José Peña", "Jose Pena")]
    [InlineData("  Ana\tLópez\n", "Ana Lopez")]
    [InlineData("Carlos Eduardo Santana Gómez", "Carlos Eduardo Santana G")]
    [InlineData("María José Hernández López", "Maria Jose Hernandez Lop")]
    [InlineData("Ana Lucía                X", "Ana Lucia")]
    [InlineData(null, "")]
    public void ForZkDevice_RemovesAccentsAndControls_AndFitsTheDeviceLimit(string? input, string expected)
    {
        var name = DeviceUserName.ForZkDevice(input);

        name.Should().Be(expected);
        name.Length.Should().BeLessThanOrEqualTo(DeviceUserName.MaxLength);
    }
}
