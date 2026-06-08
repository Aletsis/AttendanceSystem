using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.ZKTeco.Grpc;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Adapters;

public class GrpcZKTecoDiscoveryService : IDeviceDiscoveryService
{
    private readonly ZKTecoService.ZKTecoServiceClient _client;
    private readonly ILogger<GrpcZKTecoDiscoveryService> _logger;

    public GrpcZKTecoDiscoveryService(
        ZKTecoService.ZKTecoServiceClient client,
        ILogger<GrpcZKTecoDiscoveryService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveredDeviceDto>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Solicitando descubrimiento de dispositivos vía gRPC...");
            var response = await _client.DiscoverDevicesAsync(new DiscoverDevicesRequest { TimeoutSeconds = 5 }, cancellationToken: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Error en descubrimiento: {Message}", response.Message);
                return Array.Empty<DiscoveredDeviceDto>();
            }

            return response.Devices.Select(d => new DiscoveredDeviceDto(
                d.IpAddress,
                d.SerialNumber,
                d.DeviceName,
                d.MacAddress,
                d.FirmwareVersion,
                d.Port
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el descubrimiento de dispositivos gRPC");
            return Array.Empty<DiscoveredDeviceDto>();
        }
    }
}
