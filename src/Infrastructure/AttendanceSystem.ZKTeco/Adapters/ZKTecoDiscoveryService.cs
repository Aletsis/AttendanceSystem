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

                zkemkeeper.CZKEMClass? sdk = null;
                try
                {
                    sdk = new zkemkeeper.CZKEMClass();

                    string buffer = "";
                    // El método SearchDevice suele devolver una cadena con el formato:
                    // "IP=192.168.1.201,MAC=00:17:61:11:22:33,SN=8888888888888,DeviceName=iClock980,Ver=6.60,Port=4370\r\n..."
                    if (sdk.SearchDevice("UDP", "255.255.255.255", out buffer, 65536))
                    {
                        _logger.LogInformation("Dispositivos encontrados:\n{Buffer}", buffer);

                        var lines = buffer.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var device = ParseDeviceString(line);
                            if (device != null)
                            {
                                // Enriquecer el dispositivo con S/N y nombre real si faltan
                                if (!string.IsNullOrWhiteSpace(device.IpAddress))
                                {
                                    device = EnrichDeviceDetails(device);
                                }
                                devices.Add(device);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No se encontraron dispositivos ZKTeco en la red local.");
                    }
                }
                finally
                {
                    if (OperatingSystem.IsWindows() && sdk != null && Marshal.IsComObject(sdk))
                    {
                        Marshal.FinalReleaseComObject(sdk);
                    }
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
            if (string.IsNullOrWhiteSpace(line)) return null;

            // Formatos comunes devueltos por el SDK:
            // "MAC=00:17:61:11:22:33,IPAddress=192.168.1.201,Netmask=255.255.255.0,GateWay=192.168.1.1"
            // "IP=192.168.1.201,MAC=00:17:61:11:22:33,SN=8888888888888,DeviceName=iClock980,Ver=6.60,Port=4370"
            // "~SerialNumber=BAZR192360015 ~IPAddress=192.168.1.10 ~DeviceName=Main Gate ~MAC=00:17:61:12:34:56"
            var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var separators = line.Contains(',') ? new[] { ',' } : (line.Contains(';') ? new[] { ';' } : new[] { ' ', '\t' });
            var tokens = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var idx = token.IndexOf('=');
                if (idx > 0)
                {
                    var key = token.Substring(0, idx).Trim().TrimStart('~');
                    var val = token.Substring(idx + 1).Trim();
                    parts[key] = val;
                }
            }

            string GetFirstValue(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (parts.TryGetValue(k, out var v) &&
                        !string.IsNullOrWhiteSpace(v) &&
                        !v.Equals("null", StringComparison.OrdinalIgnoreCase) &&
                        !v.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        return v.Trim();
                    }
                }
                return "";
            }

            var ip = GetFirstValue("tcpip", "IPAddress", "IP", "ipaddress", "ip_address", "Host");
            var sn = GetFirstValue("SN", "SerialNumber", "serial", "serialno", "sn_no");
            var name = GetFirstValue("DeviceName", "DevName", "Device", "Name", "Model", "Product");
            var mac = GetFirstValue("MAC", "MacAddress", "mac_addr");
            var ver = GetFirstValue("Ver", "Firmware", "FirmwareVersion", "Version", "FWVersion");
            var portStr = GetFirstValue("Port", "port");

            // Si no contiene ni IP ni MAC, no es una respuesta válida de dispositivo
            if (string.IsNullOrWhiteSpace(ip) && string.IsNullOrWhiteSpace(mac))
            {
                return null;
            }

            int port = 4370;
            if (!string.IsNullOrWhiteSpace(portStr) && int.TryParse(portStr, out var p) && p > 0)
            {
                port = p;
            }

            return new DiscoveredDeviceDto(
                IpAddress: ip,
                SerialNumber: sn,
                DeviceName: !string.IsNullOrWhiteSpace(name) ? name : "Unknown",
                MacAddress: mac,
                FirmwareVersion: ver,
                Port: port
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al parsear línea de dispositivo: {Line}", line);
            return null;
        }
    }

    private DiscoveredDeviceDto EnrichDeviceDetails(DiscoveredDeviceDto device)
    {
        // Si ya tenemos S/N válido y un DeviceName específico, no es necesario conectar
        bool hasValidSn = !string.IsNullOrWhiteSpace(device.SerialNumber) && !device.SerialNumber.Equals("null", StringComparison.OrdinalIgnoreCase);
        bool hasValidName = !string.IsNullOrWhiteSpace(device.DeviceName) && !device.DeviceName.Equals("null", StringComparison.OrdinalIgnoreCase) && device.DeviceName != "Unknown";

        if (hasValidSn && hasValidName)
        {
            return device;
        }

        zkemkeeper.CZKEMClass? querySdk = null;
        try
        {
            _logger.LogInformation("Conectando temporalmente a {Ip}:{Port} para obtener número de serie y modelo...", device.IpAddress, device.Port);

            querySdk = new zkemkeeper.CZKEMClass();
            if (querySdk.Connect_Net(device.IpAddress, device.Port))
            {
                try
                {
                    // 1. Número de serie
                    string sn = "";
                    if (querySdk.GetSerialNumber(1, out sn) && !string.IsNullOrWhiteSpace(sn))
                    {
                        sn = sn.Replace("\0", "").Trim();
                        if (sn.Equals("null", StringComparison.OrdinalIgnoreCase)) sn = "";
                    }

                    // 2. Nombre / Modelo del dispositivo
                    string devName = "";
                    if (querySdk.GetSysOption(1, "~DeviceName", out devName) && !string.IsNullOrWhiteSpace(devName))
                    {
                        devName = devName.Replace("\0", "").Trim();
                    }
                    else if (querySdk.GetSysOption(1, "DeviceName", out devName) && !string.IsNullOrWhiteSpace(devName))
                    {
                        devName = devName.Replace("\0", "").Trim();
                    }
                    else
                    {
                        string platform = "";
                        if (querySdk.GetPlatform(1, ref platform) && !string.IsNullOrWhiteSpace(platform))
                        {
                            devName = platform.Replace("\0", "").Trim();
                        }
                        else
                        {
                            string product = "";
                            if (querySdk.GetProductCode(1, out product) && !string.IsNullOrWhiteSpace(product))
                            {
                                devName = product.Replace("\0", "").Trim();
                            }
                        }
                    }

                    if (devName.Equals("null", StringComparison.OrdinalIgnoreCase)) devName = "";

                    // 3. Versión de firmware
                    string fw = (!string.IsNullOrWhiteSpace(device.FirmwareVersion) && !device.FirmwareVersion.Equals("null", StringComparison.OrdinalIgnoreCase))
                        ? device.FirmwareVersion
                        : "";

                    if (string.IsNullOrWhiteSpace(fw))
                    {
                        string fwVal = "";
                        if (querySdk.GetFirmwareVersion(1, ref fwVal) && !string.IsNullOrWhiteSpace(fwVal))
                        {
                            fw = fwVal.Replace("\0", "").Trim();
                            if (fw.Equals("null", StringComparison.OrdinalIgnoreCase)) fw = "";
                        }
                    }

                    // 4. MAC address
                    string mac = (!string.IsNullOrWhiteSpace(device.MacAddress) && !device.MacAddress.Equals("null", StringComparison.OrdinalIgnoreCase))
                        ? device.MacAddress
                        : "";

                    if (string.IsNullOrWhiteSpace(mac))
                    {
                        string sMac = "";
                        if (querySdk.GetSysOption(1, "MAC", out sMac) && !string.IsNullOrWhiteSpace(sMac))
                            mac = sMac.Replace("\0", "").Trim();
                        else if (querySdk.GetSysOption(1, "~MAC", out sMac) && !string.IsNullOrWhiteSpace(sMac))
                            mac = sMac.Replace("\0", "").Trim();

                        if (mac.Equals("null", StringComparison.OrdinalIgnoreCase)) mac = "";
                    }

                    _logger.LogInformation("Detalles obtenidos para {Ip}: S/N={SerialNumber}, Nombre={DeviceName}, MAC={Mac}, FW={Firmware}",
                        device.IpAddress, sn, devName, mac, fw);

                    string finalSn = !string.IsNullOrWhiteSpace(sn) ? sn : (hasValidSn ? device.SerialNumber : "");
                    string finalName = !string.IsNullOrWhiteSpace(devName) ? devName : (hasValidName ? device.DeviceName : "Dispositivo ZKTeco");

                    return device with
                    {
                        SerialNumber = finalSn,
                        DeviceName = finalName,
                        FirmwareVersion = fw,
                        MacAddress = !string.IsNullOrWhiteSpace(mac) ? mac : device.MacAddress
                    };
                }
                finally
                {
                    try { querySdk.Disconnect(); } catch { }
                }
            }
            else
            {
                _logger.LogWarning("No se pudo conectar a {Ip}:{Port} para consultar información detallada (¿clave de comunicación establecida?)", device.IpAddress, device.Port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar información detallada de {Ip}:{Port}", device.IpAddress, device.Port);
        }
        finally
        {
            if (OperatingSystem.IsWindows() && querySdk != null && Marshal.IsComObject(querySdk))
            {
                Marshal.FinalReleaseComObject(querySdk);
            }
        }

        // Si falló la conexión (ej. firewall o contraseña), proporcionar un nombre amigable por defecto y limpiar "null"
        string fallbackName = (!string.IsNullOrWhiteSpace(device.DeviceName) && !device.DeviceName.Equals("null", StringComparison.OrdinalIgnoreCase) && device.DeviceName != "Unknown")
            ? device.DeviceName
            : "Dispositivo ZKTeco";

        string fallbackSn = (!string.IsNullOrWhiteSpace(device.SerialNumber) && !device.SerialNumber.Equals("null", StringComparison.OrdinalIgnoreCase))
            ? device.SerialNumber
            : "";

        return device with { DeviceName = fallbackName, SerialNumber = fallbackSn };
    }
}

