using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Queries.GetActiveDevices;

public sealed record GetActiveDevicesQuery : IRequest<Result<IEnumerable<DeviceDto>>>;

public sealed class GetActiveDevicesQueryHandler : IRequestHandler<GetActiveDevicesQuery, Result<IEnumerable<DeviceDto>>>
{
    private readonly IDeviceRepository _deviceRepository;

    public GetActiveDevicesQueryHandler(IDeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    public async Task<Result<IEnumerable<DeviceDto>>> Handle(GetActiveDevicesQuery request, CancellationToken cancellationToken)
    {
        var devices = await _deviceRepository.GetActiveDevicesAsync(cancellationToken);
        
        var dtos = devices.Select(d => new DeviceDto(
            d.Id.Value, 
            d.Name, 
            d.IpAddress, 
            d.Port, 
            d.Location, 
            d.IsActive, 
            d.Status.Name,
            d.DownloadMethod,
            d.LastDownloadAt, 
            d.TotalDownloadCount,
            d.ShouldClearAfterDownload
        ));

        return Result<IEnumerable<DeviceDto>>.Success(dtos);
    }
}
