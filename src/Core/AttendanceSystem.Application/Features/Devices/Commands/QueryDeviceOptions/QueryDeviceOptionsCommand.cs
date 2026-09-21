using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Devices.Queries;
using AttendanceSystem.Domain.Enumerations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Devices.Commands.QueryDeviceOptions;

public record QueryDeviceOptionsCommand(string DeviceId) : IRequest<Result>;

public class QueryDeviceOptionsHandler : IRequestHandler<QueryDeviceOptionsCommand, Result>
{
    private readonly IDeviceQueries _deviceQueries;
    private readonly IAdmsCommandService _admsCommandService;
    private readonly ILogger<QueryDeviceOptionsHandler> _logger;

    public QueryDeviceOptionsHandler(
        IDeviceQueries deviceQueries,
        IAdmsCommandService admsCommandService,
        ILogger<QueryDeviceOptionsHandler> logger)
    {
        _deviceQueries = deviceQueries;
        _admsCommandService = admsCommandService;
        _logger = logger;
    }

    public async Task<Result> Handle(QueryDeviceOptionsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceQueries.GetDeviceByIdAsync(request.DeviceId, cancellationToken);
            if (device == null)
            {
                return Result.Failure($"Dispositivo {request.DeviceId} no encontrado.");
            }

            if (device.DownloadMethod == DeviceDownloadMethod.Adms)
            {
                var sn = device.SerialNumber;
                if (string.IsNullOrWhiteSpace(sn))
                {
                    return Result.Failure("El dispositivo ADMS no tiene número de serie registrado.");
                }

                _logger.LogInformation("ADMS: Encolando consulta de opciones (DATA QUERY tablename=options) para SN: {SerialNumber}", sn);

                _admsCommandService.EnqueueCommand(sn, "DATA QUERY tablename=options");
                _admsCommandService.EnqueueCommand(sn, "CHECK");

                return Result.Success();
            }

            return Result.Failure("La consulta de tabla de opciones ADMS solo aplica para dispositivos configurados en modo ADMS (Push).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al encolar consulta de opciones para dispositivo {DeviceId}", request.DeviceId);
            return Result.Failure($"Error al encolar consulta: {ex.Message}");
        }
    }
}
