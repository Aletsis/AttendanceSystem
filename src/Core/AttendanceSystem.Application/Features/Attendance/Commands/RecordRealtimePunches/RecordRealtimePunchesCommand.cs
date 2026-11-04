using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RecordRealtimePunches;

/// <summary>Marcaje de un reloj SDK recibido en tiempo real. <see cref="CheckTime"/> es la hora civil que marca el reloj.</summary>
public sealed record RealtimePunchDto(string UserId, DateTime CheckTime, int VerifyMethod, int InOutMode);

/// <summary>
/// Guarda los marcajes que un reloj SDK envió en tiempo real, igual que los de una descarga: los de sucursales externas
/// quedan pendientes de subir a la nube y los demás pasan por la cuarentena (como llegada en tiempo real) y el registro.
/// No mueve la fecha de la última descarga: la descarga siguiente vuelve a leer desde ahí y recupera lo que la escucha
/// no haya recibido.
/// </summary>
public sealed record RecordRealtimePunchesCommand(string DeviceId, IReadOnlyList<RealtimePunchDto> Punches) : IRequest<Result<int>>;

public sealed class RecordRealtimePunchesCommandHandler : IRequestHandler<RecordRealtimePunchesCommand, Result<int>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IExternalAttendanceLogRepository _externalLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly IMediator _mediator;
    private readonly IDeviceHeartbeatTracker? _heartbeats;
    private readonly ILogger<RecordRealtimePunchesCommandHandler> _logger;

    public RecordRealtimePunchesCommandHandler(
        IDeviceRepository deviceRepository,
        IBranchRepository branchRepository,
        IExternalAttendanceLogRepository externalLogRepository,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver timeZoneResolver,
        IMediator mediator,
        ILogger<RecordRealtimePunchesCommandHandler> logger,
        IDeviceHeartbeatTracker? heartbeats = null)
    {
        _deviceRepository = deviceRepository;
        _branchRepository = branchRepository;
        _externalLogRepository = externalLogRepository;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver;
        _mediator = mediator;
        _logger = logger;
        _heartbeats = heartbeats;
    }

    public async Task<Result<int>> Handle(RecordRealtimePunchesCommand command, CancellationToken cancellationToken)
    {
        if (command.Punches.Count == 0) return Result<int>.Success(0);

        var deviceId = DeviceId.From(command.DeviceId);
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device == null || !device.IsActive)
        {
            _logger.LogWarning("Tiempo real: se ignoraron {Count} marcajes del reloj {DeviceId} (no existe o está inactivo).", command.Punches.Count, command.DeviceId);
            return Result<int>.Failure("Dispositivo no encontrado o inactivo");
        }

        // Caso 18: la regla R4 (reloj atrasado en tiempo real) necesita saber desde cuándo se comunica el reloj.
        _heartbeats?.RecordActivity(device.HardwareInfo?.SerialNumber ?? string.Empty, device.Id.Value);

        var timeZone = await _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);
        var source = device.Brand.ToString();
        var externalBranches = (await _branchRepository.GetAllAsync(cancellationToken))
            .Where(b => b.IsExternal)
            .GroupBy(b => b.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var localLogs = new List<AttendanceLogDto>();
        var externalLogs = new List<ExternalAttendanceLog>();
        foreach (var punch in command.Punches)
        {
            var utc = _timeNormalizer.ToUtc(punch.CheckTime, timeZone);

            // Igual que la descarga: un ID con el código de 3 letras de una sucursal externa es un empleado de esa sucursal.
            if (punch.UserId.Length > 3 && externalBranches.ContainsKey(punch.UserId[..3]))
            {
                externalLogs.Add(ExternalAttendanceLog.Create(
                    punch.UserId[..3], punch.UserId[3..], utc, punch.VerifyMethod, punch.InOutMode,
                    device.HardwareInfo?.SerialNumber ?? command.DeviceId));
                continue;
            }

            localLogs.Add(new AttendanceLogDto(
                punch.UserId,
                utc,
                punch.VerifyMethod,
                punch.InOutMode,
                OriginalCheckTime: punch.CheckTime,
                WorkDate: _timeNormalizer.ToWorkDate(utc, timeZone),
                TimeZoneId: timeZone,
                Source: source));
        }

        var stored = 0;
        if (externalLogs.Count > 0)
        {
            var newExternalLogs = await _externalLogRepository.ExcludeAlreadyStoredAsync(externalLogs, cancellationToken);
            foreach (var externalLog in newExternalLogs)
            {
                await _externalLogRepository.AddAsync(externalLog, cancellationToken);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            stored += newExternalLogs.Count;
        }

        if (localLogs.Count > 0)
        {
            // Sin número de serie: la resincronización de hora de un reloj atrasado encola comandos ADMS, que un reloj
            // SDK nunca recoge. La regla R4 lo encuentra por su identificador.
            var result = await _mediator.Send(new RecordAttendanceBatchCommand(
                command.DeviceId,
                localLogs,
                timeZone,
                source,
                Arrival: AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrival.Realtime,
                DeviceSerial: null), cancellationToken);
            if (!result.IsSuccess) return result;
            stored += result.Value;
        }

        _logger.LogInformation("Tiempo real: {Stored} de {Count} marcajes del reloj {Device} guardados ({External} de sucursales externas).",
            stored, command.Punches.Count, device.Name, externalLogs.Count);
        return Result<int>.Success(stored);
    }
}
