using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Commands.ClearDeviceLogs;

public record ClearDeviceLogsCommand(string DeviceId, DateTime? FromDate = null, DateTime? ToDate = null) : IRequest<Result>;

public class ClearDeviceLogsHandler : IRequestHandler<ClearDeviceLogsCommand, Result>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceQueries _deviceQueries;

    public ClearDeviceLogsHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceQueries deviceQueries)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceQueries = deviceQueries;
    }

    public async Task<Result> Handle(ClearDeviceLogsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceQueries.GetDeviceByIdAsync(request.DeviceId, cancellationToken);
            if (device == null)
            {
                return Result.Failure($"Device.NotFound: Device {request.DeviceId} not found");
            }

            var deviceClient = _deviceClientFactory.GetClient(device);
            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            if (!connected)
            {
                return Result.Failure($"Device.ConnectionFailed: Could not connect to device at {device.IpAddress}");
            }

            try
            {
                var success = await deviceClient.ClearLogsAsync(device.DeviceId, request.FromDate, request.ToDate, cancellationToken);
                return success 
                    ? Result.Success() 
                    : Result.Failure("Device.ClearLogsFailed: Device returned failure status");
            }
            finally
            {
                await deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure($"Device.ClearLogsException: {ex.Message}");
        }
    }
}
