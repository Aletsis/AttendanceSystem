using AttendanceSystem.Application.Abstractions;
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
    string? InitiatedByUserName = null,
    bool ForceFullSync = false) : IRequest<Result<IEnumerable<DownloadResultDto>>>;

public sealed class DownloadFromAllDevicesCommandHandler : IRequestHandler<DownloadFromAllDevicesCommand, Result<IEnumerable<DownloadResultDto>>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IMediator _mediator;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IAttendanceConsolidationRepository _consolidationRepository;
    private readonly IConsolidationWakeup _wakeup;
    private readonly ILogger<DownloadFromAllDevicesCommandHandler> _logger;

    public DownloadFromAllDevicesCommandHandler(
        IDeviceRepository deviceRepository,
        IMediator mediator,
        IEmployeeRepository employeeRepository,
        IAttendanceConsolidationRepository consolidationRepository,
        IConsolidationWakeup wakeup,
        ILogger<DownloadFromAllDevicesCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _mediator = mediator;
        _employeeRepository = employeeRepository;
        _consolidationRepository = consolidationRepository;
        _wakeup = wakeup;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<DownloadResultDto>>> Handle(DownloadFromAllDevicesCommand request, CancellationToken cancellationToken)
    {
        var devices = await _deviceRepository.GetActiveDevicesAsync(cancellationToken);
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
                request.InitiatedByUserName,
                request.ForceFullSync);

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

            // F04-04 / F04-05: solicitudes durables por empleado (agrupadas por el consumidor) en lugar de un
            // trabajo en memoria que se pierde si el proceso cae antes de ejecutarlo.
            var employees = await _employeeRepository.GetAllAsync(cancellationToken);
            var activeIds = employees
                .Where(e => e.IsActiveDuring(processStartDate, globalMaxDate.Value))
                .Select(e => e.Id)
                .ToList();

            _logger.LogInformation("Solicitando consolidación GLOBAL de asistencia para detectar faltas. Empleados: {Count}. Rango: {Start} - {End}",
                activeIds.Count, processStartDate, globalMaxDate.Value);
            await _consolidationRepository.RequestManyAsync(activeIds, processStartDate, globalMaxDate.Value, cancellationToken);
            _wakeup.Signal();
        }

        return Result<IEnumerable<DownloadResultDto>>.Success(results);
    }
}
