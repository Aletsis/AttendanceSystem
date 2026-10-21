using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Clock;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime;

/// <summary>
/// Sincroniza el reloj con la hora actual de SU zona horaria (y, en ADMS/Hikvision, también su desfase).
/// <paramref name="CivilTime"/> permite fijar una hora concreta, expresada en la zona del reloj.
/// </summary>
public record SetDeviceTimeCommand(string DeviceId, DateTime? CivilTime = null) : IRequest<Result>;

public class SetDeviceTimeHandler : IRequestHandler<SetDeviceTimeCommand, Result>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceQueries _deviceQueries;
    private readonly DeviceClockService _clock;
    private readonly IDeviceLockService? _deviceLockService;

    public SetDeviceTimeHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceQueries deviceQueries,
        DeviceClockService? clock = null,
        IDeviceLockService? deviceLockService = null)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceQueries = deviceQueries;
        _clock = clock ?? new DeviceClockService(DefaultTimeNormalizer.Instance, DefaultAuthoritativeTimeZoneResolver.Instance);
        _deviceLockService = deviceLockService;
    }

    public async Task<Result> Handle(SetDeviceTimeCommand request, CancellationToken cancellationToken)
    {
        if (_deviceLockService == null) return await SyncAsync(request, cancellationToken);

        // Misma sesión que la descarga: no se abre una segunda conexión al reloj mientras otra está activa.
        Result result = Result.Failure("Device.SetTimeException: no se ejecutó la sincronización");
        await _deviceLockService.ExecuteWithLockAsync(request.DeviceId, async () => result = await SyncAsync(request, cancellationToken), cancellationToken);
        return result;
    }

    private async Task<Result> SyncAsync(SetDeviceTimeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceQueries.GetDeviceByIdAsync(request.DeviceId, cancellationToken);
            if (device == null)
            {
                return Result.Failure($"Device.NotFound: Device {request.DeviceId} not found");
            }

            var timeZoneId = await _clock.TimeZoneOfAsync(DeviceId.From(device.DeviceId), cancellationToken);

            // 0. Obtener cliente específico
            var deviceClient = _deviceClientFactory.GetClient(device);

            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            if (!connected)
            {
                return Result.Failure($"Device.ConnectionFailed: Could not connect to device at {device.IpAddress}");
            }

            try
            {
                var success = request.CivilTime is { } civilTime
                    ? await deviceClient.SetDeviceClockAsync(DateTime.SpecifyKind(civilTime, DateTimeKind.Unspecified), _clock.UtcOffsetOf(timeZoneId), cancellationToken)
                    : await _clock.SyncAsync(deviceClient, timeZoneId, device.Name, cancellationToken);
                return success
                    ? Result.Success()
                    : Result.Failure("Device.SetTimeFailed: Device returned failure status");
            }
            finally
            {
                await deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure($"Device.SetTimeException: {ex.Message}");
        }
    }
}
