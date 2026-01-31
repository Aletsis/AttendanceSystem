using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Commands.SetDeviceTime;

public record SetDeviceTimeCommand(string DeviceId, DateTime DateTime) : IRequest<Result>;

public class SetDeviceTimeHandler : IRequestHandler<SetDeviceTimeCommand, Result>
{
    private readonly IZKTecoDeviceClient _deviceClient;
    private readonly IDeviceQueries _deviceQueries;

    public SetDeviceTimeHandler(
        IZKTecoDeviceClient deviceClient,
        IDeviceQueries deviceQueries)
    {
        _deviceClient = deviceClient;
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

            var connected = await _deviceClient.ConnectAsync(device.IpAddress, device.Port, cancellationToken);
            if (!connected)
            {
                return Result.Failure($"Device.ConnectionFailed: Could not connect to device at {device.IpAddress}");
            }

            try
            {
                var success = await _deviceClient.SetDeviceTimeAsync(request.DateTime, cancellationToken);
                return success 
                    ? Result.Success() 
                    : Result.Failure("Device.SetTimeFailed: Device returned failure status");
            }
            finally
            {
                await _deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure($"Device.SetTimeException: {ex.Message}");
        }
    }
}
