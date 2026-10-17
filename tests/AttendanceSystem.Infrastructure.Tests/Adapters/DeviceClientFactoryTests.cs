using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Infrastructure.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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

    [Fact]
    public void GetClient_ForZKTeco_ShouldResolveTransientClientsWithDistinctInstances_H15()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AttendanceDb", "Host=localhost;Database=test;Username=postgres;Password=pass" },
                { "ZKTecoService:ApiKey", "test-key" }
            })
            .Build();

        services.AddLogging();
        AttendanceSystem.Infrastructure.DependencyInjection.InfrastructureServiceExtensions.AddInfrastructureServices(services, config);

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDeviceClientFactory>();

        var client1 = factory.GetClient(DeviceBrand.ZKTeco);
        var client2 = factory.GetClient(DeviceBrand.ZKTeco);

        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
        // Transient: deben ser instancias distintas en cada llamada (H15)
        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public void GetClient_ForAdms_ShouldResolveTransientClientsWithDistinctInstances_H15()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AttendanceDb", "Host=localhost;Database=test;Username=postgres;Password=pass" }
            })
            .Build();

        services.AddLogging();
        AttendanceSystem.Infrastructure.DependencyInjection.InfrastructureServiceExtensions.AddInfrastructureServices(services, config);

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDeviceClientFactory>();

        var client1 = factory.GetClient(new DeviceDto { DownloadMethod = DeviceDownloadMethod.Adms, SerialNumber = "SN001" });
        var client2 = factory.GetClient(new DeviceDto { DownloadMethod = DeviceDownloadMethod.Adms, SerialNumber = "SN002" });

        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
        client1.Should().NotBeSameAs(client2);
    }
}
