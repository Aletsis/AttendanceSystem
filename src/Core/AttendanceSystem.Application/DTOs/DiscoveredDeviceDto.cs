namespace AttendanceSystem.Application.DTOs;

public record DiscoveredDeviceDto(
    string IpAddress,
    string SerialNumber,
    string DeviceName,
    string MacAddress,
    string FirmwareVersion,
    int Port);
