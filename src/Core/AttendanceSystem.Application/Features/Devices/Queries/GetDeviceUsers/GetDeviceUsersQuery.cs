using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Application.DTOs;
using MediatR;
using AttendanceSystem.Application.Common;

namespace AttendanceSystem.Application.Features.Devices.Queries.GetDeviceUsers;

public record GetDeviceUsersQuery(string DeviceId) : IRequest<Result<IReadOnlyList<DeviceUserDto>>>;

public class GetDeviceUsersHandler : IRequestHandler<GetDeviceUsersQuery, Result<IReadOnlyList<DeviceUserDto>>>
{
    private readonly IZKTecoDeviceClient _deviceClient;
    private readonly IDeviceQueries _deviceQueries;

    public GetDeviceUsersHandler(
        IZKTecoDeviceClient deviceClient,
        IDeviceQueries deviceQueries)
    {
        _deviceClient = deviceClient;
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

            var connected = await _deviceClient.ConnectAsync(device.IpAddress, device.Port, cancellationToken);
            if (!connected)
            {
                return Result<IReadOnlyList<DeviceUserDto>>.Failure($"Device.ConnectionFailed: Could not connect to device at {device.IpAddress}");
            }

            try
            {
                var users = await _deviceClient.GetAllUsersAsync(cancellationToken);
                return Result<IReadOnlyList<DeviceUserDto>>.Success(users);
            }
            finally
            {
                await _deviceClient.DisconnectAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<DeviceUserDto>>.Failure($"Device.GetUsersFailure: {ex.Message}");
        }
    }
}
