using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime;

public record SetDeviceTimeCommand(string DeviceId, DateTime DateTime) : IRequest<Result>;

public class SetDeviceTimeHandler : IRequestHandler<SetDeviceTimeCommand, Result>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceQueries _deviceQueries;

    public SetDeviceTimeHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceQueries deviceQueries)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceQueries = deviceQueries;
    }

    public async Task<Result> Handle(SetDeviceTimeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceQueries.GetDeviceByIdAsync(request.DeviceId, cancellationToken);
            if (device == null)
            {
                return Result.Failure($"Device.NotFound: Device {request.DeviceId} not found");
            }

            // 0. Obtener cliente específico
            var deviceClient = _deviceClientFactory.GetClient(device.Brand);

            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            if (!connected)
            {
                return Result.Failure($"Device.ConnectionFailed: Could not connect to device at {device.IpAddress}");
            }

            try
            {
                var success = await deviceClient.SetDeviceTimeAsync(request.DateTime, cancellationToken);
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
