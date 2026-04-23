using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;

namespace AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromAllDevices;

public sealed record DownloadFromAllDevicesCommand(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? InitiatedByUserId = null,
    string? InitiatedByUserName = null) : IRequest<Result<IEnumerable<DownloadResultDto>>>;

public sealed class DownloadFromAllDevicesCommandHandler : IRequestHandler<DownloadFromAllDevicesCommand, Result<IEnumerable<DownloadResultDto>>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IMediator _mediator;

    public DownloadFromAllDevicesCommandHandler(IDeviceRepository deviceRepository, IMediator mediator)
    {
        _deviceRepository = deviceRepository;
        _mediator = mediator;
    }

    public async Task<Result<IEnumerable<DownloadResultDto>>> Handle(DownloadFromAllDevicesCommand request, CancellationToken cancellationToken)
    {
        var allActiveDevices = await _deviceRepository.GetActiveDevicesAsync(cancellationToken);
        var devices = allActiveDevices.Where(d => d.DownloadMethod != AttendanceSystem.Domain.Enumerations.DeviceDownloadMethod.Adms).ToList();
        var results = new List<DownloadResultDto>();
        
        DateTime? globalMinDate = null;
        DateTime? globalMaxDate = null;

        var allAffectedEmployeeIds = new HashSet<string>();

        foreach (var device in devices)
        {
            var command = new DownloadFromDeviceCommand(
                device.Id.Value, 
                request.FromDate, 
                request.ToDate, 
                CalculateAttendance: false,
                request.InitiatedByUserId,
                request.InitiatedByUserName);

            var result = await _mediator.Send(command, cancellationToken);
            
            if (result.IsSuccess)
            {
                results.Add(result.Value);
                
                if (result.Value.MinDate.HasValue)
                {
                    if (globalMinDate == null || result.Value.MinDate < globalMinDate)
                        globalMinDate = result.Value.MinDate;
                }
                
                if (result.Value.MaxDate.HasValue)
                {
                    if (globalMaxDate == null || result.Value.MaxDate > globalMaxDate)
                        globalMaxDate = result.Value.MaxDate;
                }

                if (result.Value.AffectedEmployeeIds != null)
                {
                    foreach (var id in result.Value.AffectedEmployeeIds)
                    {
                        allAffectedEmployeeIds.Add(id);
                    }
                }
            }
            else
            {
                results.Add(new DownloadResultDto(
                    DeviceId: device.Id.Value, 
                    RecordsDownloaded: 0, 
                    DownloadedAt: DateTime.UtcNow, 
                    Success: false, 
                    ErrorMessage: result.Error));
            }
        }
        
        if (allAffectedEmployeeIds.Any() && globalMinDate.HasValue && globalMaxDate.HasValue)
        {
            foreach (var empId in allAffectedEmployeeIds)
            {
                await _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.ProcessDailyAttendanceCommand(
                    globalMinDate.Value, 
                    globalMaxDate.Value, 
                    EmployeeId: EmployeeId.From(empId)), cancellationToken);
            }
        }

        return Result<IEnumerable<DownloadResultDto>>.Success(results);
    }
}
