using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace AttendanceSystem.ZKTeco.Adapters;

public class ZKTecoDiscoveryService : IDeviceDiscoveryService
{
    private readonly ILogger<ZKTecoDiscoveryService> _logger;

    public ZKTecoDiscoveryService(ILogger<ZKTecoDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveredDeviceDto>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var devices = new List<DiscoveredDeviceDto>();
            try
            {
                _logger.LogInformation("Iniciando búsqueda de dispositivos ZKTeco en la red local...");
                
                // Usamos el objeto COM para la búsqueda
                // Nota: Algunos SDKs requieren que el objeto sea CZKEMClass
                var sdk = new zkemkeeper.CZKEMClass();
                
                string buffer = "";
                // El método SearchDevice suele devolver una cadena con el formato:
                // "IP=192.168.1.201,MAC=00:17:61:11:22:33,SN=8888888888888,DeviceName=iClock980,Ver=6.60,Port=4370\r\n..."
                if (sdk.SearchDevice("UDP", "255.255.255.255", out buffer, 65536))
                {
                    _logger.LogInformation("Dispositivos encontrados:\n{Buffer}", buffer);
                    
                    var lines = buffer.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var device = ParseDeviceString(line);
                        if (device != null)
                        {
                            devices.Add(device);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No se encontraron dispositivos ZKTeco en la red local.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la búsqueda de dispositivos");
            }

            return (IReadOnlyList<DiscoveredDeviceDto>)devices;
        }, cancellationToken);
    }

    private DiscoveredDeviceDto? ParseDeviceString(string line)
    {
        try
        {
            // Ejemplo: IP=192.168.1.201,MAC=00:17:61:11:22:33,SN=8888888888888,DeviceName=iClock980,Ver=6.60,Port=4370
            var parts = line.Split(',').Select(p => p.Split('=')).ToDictionary(kv => kv[0].Trim(), kv => kv.Length > 1 ? kv[1].Trim() : "");

            return new DiscoveredDeviceDto(
                IpAddress: parts.GetValueOrDefault("IP", ""),
                SerialNumber: parts.GetValueOrDefault("SN", ""),
                DeviceName: parts.GetValueOrDefault("DeviceName", "Unknown"),
                MacAddress: parts.GetValueOrDefault("MAC", ""),
                FirmwareVersion: parts.GetValueOrDefault("Ver", ""),
                Port: int.TryParse(parts.GetValueOrDefault("Port", "4370"), out var p) ? p : 4370
            );
        }
        catch
        {
            return null;
        }
    }
}
