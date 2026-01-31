using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Commands.DeleteDeviceUserFingerprints;

public record DeleteDeviceUserFingerprintsCommand(string DeviceId, string UserId) : IRequest<Result>;

public class DeleteDeviceUserFingerprintsHandler : IRequestHandler<DeleteDeviceUserFingerprintsCommand, Result>
{
    private readonly IZKTecoDeviceClient _deviceClient;
    private readonly IDeviceQueries _deviceQueries;

    public DeleteDeviceUserFingerprintsHandler(
        IZKTecoDeviceClient deviceClient,
        IDeviceQueries deviceQueries)
    {
        _deviceClient = deviceClient;
        _deviceQueries = deviceQueries;
    }

    public async Task<Result> Handle(DeleteDeviceUserFingerprintsCommand request, CancellationToken cancellationToken)
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
                var success = await _deviceClient.DeleteUserFingerprintsAsync(request.UserId, cancellationToken);
                return success 
                    ? Result.Success() 
                    : Result.Failure("Device.DeleteFingerprintsFailed: Device returned failure status");
            }
            finally
            {
                await _deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure($"Device.DeleteFingerprintsException: {ex.Message}");
        }
    }
}
