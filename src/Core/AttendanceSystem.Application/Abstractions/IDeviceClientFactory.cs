using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Abstractions;

public interface IDeviceClientFactory
{
    /// <summary>
    /// Obtiene el cliente apropiado para un dispositivo específico.
    /// </summary>
    IDeviceClient GetClient(DeviceBrand brand);

    /// <summary>
    /// Obtiene el cliente apropiado basado en la entidad Device.
    /// </summary>
    IDeviceClient GetClient(Device device);

    /// <summary>
    /// Obtiene el cliente apropiado basado en el DTO DeviceDto.
    /// </summary>
    IDeviceClient GetClient(DeviceDto device);
}
