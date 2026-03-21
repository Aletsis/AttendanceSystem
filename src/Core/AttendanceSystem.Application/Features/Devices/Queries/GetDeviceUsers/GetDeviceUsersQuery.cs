using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Application.DTOs;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Queries.GetDeviceUsers;

public record GetDeviceUsersQuery(string DeviceId) : IRequest<Result<IReadOnlyList<DeviceUserDto>>>;

public class GetDeviceUsersHandler : IRequestHandler<GetDeviceUsersQuery, Result<IReadOnlyList<DeviceUserDto>>>
{
    private readonly IDeviceClientFactory _deviceClientFactory;
    private readonly IDeviceQueries _deviceQueries;

    public GetDeviceUsersHandler(
        IDeviceClientFactory deviceClientFactory,
        IDeviceQueries deviceQueries)
    {
        _deviceClientFactory = deviceClientFactory;
        _deviceQueries = deviceQueries;
    }

    public async Task<Result<IReadOnlyList<DeviceUserDto>>> Handle(GetDeviceUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceQueries.GetDeviceByIdAsync(request.DeviceId, cancellationToken);
            if (device == null)
            {
                return Result<IReadOnlyList<DeviceUserDto>>.Failure($"Device.NotFound: Device {request.DeviceId} not found");
            }

            var deviceClient = _deviceClientFactory.GetClient(device);
            var connected = await deviceClient.ConnectAsync(device.IpAddress, device.Port, device.Username, device.Password, cancellationToken);
            if (!connected)
            {
                return Result<IReadOnlyList<DeviceUserDto>>.Failure($"Device.ConnectionFailed: Could not connect to device at {device.IpAddress}");
            }

            try
            {
                var users = await deviceClient.GetAllUsersAsync(cancellationToken);
                return Result<IReadOnlyList<DeviceUserDto>>.Success(users);
            }
            finally
            {
                await deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<DeviceUserDto>>.Failure($"Device.GetUsersFailure: {ex.Message}");
        }
    }
}
