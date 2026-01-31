using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Queries.GetAllDevices;

public sealed record GetAllDevicesQuery : IRequest<Result<IEnumerable<DeviceDto>>>;

public sealed class GetAllDevicesQueryHandler : IRequestHandler<GetAllDevicesQuery, Result<IEnumerable<DeviceDto>>>
{
    private readonly IDeviceQueries _deviceQueries;

    public GetAllDevicesQueryHandler(IDeviceQueries deviceQueries)
    {
        _deviceQueries = deviceQueries;
    }

    public async Task<Result<IEnumerable<DeviceDto>>> Handle(GetAllDevicesQuery request, CancellationToken cancellationToken)
    {
        var dtos = await _deviceQueries.GetAllDevicesAsync(cancellationToken);
        return Result<IEnumerable<DeviceDto>>.Success(dtos);
    }
}
