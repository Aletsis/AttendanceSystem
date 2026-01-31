using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Commands.ResetDeviceToFactory;

public record ResetDeviceToFactoryCommand(string DeviceId) : IRequest<Result>;

public class ResetDeviceToFactoryHandler : IRequestHandler<ResetDeviceToFactoryCommand, Result>
{
    private readonly IZKTecoDeviceClient _deviceClient;
    private readonly IDeviceQueries _deviceQueries;

    public ResetDeviceToFactoryHandler(
        IZKTecoDeviceClient deviceClient,
        IDeviceQueries deviceQueries)
    {
        _deviceClient = deviceClient;
        _deviceQueries = deviceQueries;
    }

    public async Task<Result> Handle(ResetDeviceToFactoryCommand request, CancellationToken cancellationToken)
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
                var success = await _deviceClient.ResetToFactorySettingsAsync(cancellationToken);
                return success 
                    ? Result.Success() 
                    : Result.Failure("Device.ResetFailed: Device returned failure status");
            }
            finally
            {
                await _deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure($"Device.ResetException: {ex.Message}");
        }
    }
}
