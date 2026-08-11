using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Enumerations;
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
    private readonly IAttendanceJobScheduler _jobScheduler;
    private readonly ILogger<DownloadFromAllDevicesCommandHandler> _logger;

    public DownloadFromAllDevicesCommandHandler(
        IDeviceRepository deviceRepository, 
        IMediator mediator,
        IAttendanceJobScheduler jobScheduler,
        ILogger<DownloadFromAllDevicesCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _mediator = mediator;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<DownloadResultDto>>> Handle(DownloadFromAllDevicesCommand request, CancellationToken cancellationToken)
    {
        var allActiveDevices = await _deviceRepository.GetActiveDevicesAsync(cancellationToken);
        var devices = allActiveDevices.Where(d => d.DownloadMethod != DeviceDownloadMethod.Adms).ToList();
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
        
        if (globalMinDate.HasValue && globalMaxDate.HasValue)
        {
            // Al descargar de TODOS los dispositivos, disparamos un proceso GLOBAL (null employeeId)
            // Esto asegura que se detecten las FALTAS de quienes no registraron nada.
            // Expandimos 1 día atrás para turnos nocturnos.
            var processStartDate = globalMinDate.Value.AddDays(-1);
            
            _logger.LogInformation("Encolando procesamiento GLOBAL de asistencia para detectar faltas. Rango: {Start} - {End}", processStartDate, globalMaxDate.Value);
            _jobScheduler.EnqueueAttendanceProcessing(processStartDate, globalMaxDate.Value, null);
        }

        return Result<IEnumerable<DownloadResultDto>>.Success(results);
    }
}
