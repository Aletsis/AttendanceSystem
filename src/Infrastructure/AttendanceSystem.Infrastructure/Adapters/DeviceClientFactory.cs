using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.Infrastructure.Adapters;

public class DeviceClientFactory : IDeviceClientFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DeviceClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IDeviceClient GetClient(DeviceBrand brand)
    {
        return brand switch
        {
            DeviceBrand.ZKTeco => _serviceProvider.GetRequiredService<GrpcZKTecoDeviceClient>(),
            DeviceBrand.Hikvision => _serviceProvider.GetRequiredService<HikvisionDeviceClient>(),
            _ => throw new ArgumentException($"Marca de dispositivo no soportada: {brand}")
        };
    }
}
