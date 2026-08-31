using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Infrastructure.Adapters;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Adapters;

public class DeviceClientFactoryTests
{
    [Fact]
    public void GetClient_WhenBrandIsUnsupported_ShouldThrowArgumentException()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var factory = new DeviceClientFactory(serviceProviderMock.Object);

        // Act
        Action act = () => factory.GetClient((DeviceBrand)999);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Marca de dispositivo no soportada*");
    }
}
