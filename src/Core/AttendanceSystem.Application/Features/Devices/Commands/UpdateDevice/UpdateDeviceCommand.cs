using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Repositories;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Commands.UpdateDevice;

public sealed record UpdateDeviceCommand(
    Guid Id,
    string Name,
    string IpAddress,
    int Port,
    string? Location,
    bool ShouldClearAfterDownload,
    DeviceDownloadMethod DownloadMethod) : IRequest<Result>;

public sealed class UpdateDeviceCommandHandler : IRequestHandler<UpdateDeviceCommand, Result>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDeviceCommandHandler(IDeviceRepository deviceRepository, IUnitOfWork unitOfWork)
    {
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateDeviceCommand request, CancellationToken cancellationToken)
    {
        var deviceId = DeviceId.From(request.Id.ToString());
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return Result.Failure("El dispositivo no fue encontrado");
        }

        device.UpdateConfiguration(
            request.Name,
            request.IpAddress,
            request.Port,
            request.Location,
            request.ShouldClearAfterDownload,
            request.DownloadMethod);

        await _deviceRepository.UpdateAsync(device, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
