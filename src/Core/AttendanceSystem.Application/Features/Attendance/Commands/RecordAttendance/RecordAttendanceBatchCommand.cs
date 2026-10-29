using System.Collections.Generic;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Application.Features.Attendance.Services;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;

public sealed record AttendanceLogDto(
    string EmployeeId,
    DateTime CheckTime,
    int VerifyMethodCode,
    int CheckTypeCode,
    DateTime? OriginalCheckTime = null,
    DateOnly? WorkDate = null,
    string? TimeZoneId = null,
    string? Source = null
);

public sealed record RecordAttendanceBatchCommand(
    string DeviceId,
    List<AttendanceLogDto> Logs,
    string? TimeZoneId = null,
    string? Source = null,
    Guid? DownloadLogId = null,
    // Caso 18: tiempo real (ADMS empuja cada checada) o respuesta a una consulta/re-descarga.
    AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrival Arrival = AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrival.Redownload,
    string? DeviceSerial = null
) : IRequest<Result<int>>;

// Handler
public sealed class RecordAttendanceBatchCommandHandler
    : IRequestHandler<RecordAttendanceBatchCommand, Result<int>>
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ILogger<RecordAttendanceBatchCommandHandler> _logger;
    private readonly IPersistenceFailureClassifier _failureClassifier;
    private readonly AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService? _quarantine;
    private readonly IAdmsCommandService? _admsCommands;
    private readonly IDeviceRepository? _devices;
    private readonly AttendanceSystem.Domain.Repositories.ISystemAlertRepository? _alerts;

    public RecordAttendanceBatchCommandHandler(
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        ILogger<RecordAttendanceBatchCommandHandler>? logger)
        : this(attendanceRepository, unitOfWork, timeNormalizer, DefaultAuthoritativeTimeZoneResolver.Instance, logger)
    {
    }

    public RecordAttendanceBatchCommandHandler(
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver? timeZoneResolver,
        ILogger<RecordAttendanceBatchCommandHandler>? logger = null,
        IPersistenceFailureClassifier? failureClassifier = null,
        AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService? quarantine = null,
        IAdmsCommandService? admsCommands = null,
        IDeviceRepository? devices = null,
        AttendanceSystem.Domain.Repositories.ISystemAlertRepository? alerts = null)
    {
        _admsCommands = admsCommands;
        _devices = devices;
        _alerts = alerts;
        _quarantine = quarantine;
        _failureClassifier = failureClassifier ?? DefaultPersistenceFailureClassifier.Instance;
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordAttendanceBatchCommandHandler>.Instance;
    }

    public async Task<Result<int>> Handle(
        RecordAttendanceBatchCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Logs == null || command.Logs.Count == 0)
        {
            return Result<int>.Success(0);
        }

        try
        {
            var deviceId = DeviceId.From(command.DeviceId);
            var defaultBatchTz = !string.IsNullOrWhiteSpace(command.TimeZoneId)
                ? command.TimeZoneId
                : await _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);

            // 1. Normalizar todos los registros a UTC y deduplicar el lote internamente
            var normalizedLogs = new List<(AttendanceLogDto Original, DateTime UtcTime, DateOnly WorkDate, string ResolvedTz)>();
            var inBatchKeys = new HashSet<(string EmployeeId, DateTime UtcTime)>();
            int inBatchDuplicates = 0;

            foreach (var log in command.Logs)
            {
                var tz = log.TimeZoneId ?? defaultBatchTz;
                var utcTime = _timeNormalizer.ToUtc(log.CheckTime, tz);
                var workDate = log.WorkDate ?? _timeNormalizer.ToWorkDate(utcTime, tz);

                if (!inBatchKeys.Add((log.EmployeeId, utcTime)))
                {
                    inBatchDuplicates++;
                    continue;
                }

                normalizedLogs.Add((log, utcTime, workDate, tz));
            }

            // Marcajes con hora imposible (reloj sin fecha o adelantado): a cuarentena, no al cálculo. Cuentan como
            // recibidos para que el reloj no los reenvíe.
            int quarantined = 0;
            if (_quarantine != null && normalizedLogs.Count > 0)
            {
                var punches = normalizedLogs.Select(n => new AttendanceSystem.Application.Features.Devices.Quarantine.PunchCandidate(
                    n.Original.EmployeeId, CivilOf(n.Original, n.ResolvedTz), n.ResolvedTz, n.Original.VerifyMethodCode, n.Original.CheckTypeCode,
                    n.Original.Source ?? command.Source ?? "ADMS")).ToList();
                var evaluation = await _quarantine.EvaluateAsync(deviceId, command.DeviceId, punches,
                    new AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrivalContext(command.Arrival, DeviceSerial: command.DeviceSerial), cancellationToken);
                if (evaluation.Excluded.Count > 0)
                {
                    quarantined = evaluation.Excluded.Count;
                    normalizedLogs = normalizedLogs.Where((_, i) => !evaluation.Excluded.Contains(i)).ToList();
                }
                if (evaluation.ClockBehind is { } behind && command.Arrival == AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrival.Realtime)
                {
                    await HandleClockBehindAsync(deviceId, command, defaultBatchTz, behind, evaluation, punches, cancellationToken);
                }
            }

            if (normalizedLogs.Count == 0)
            {
                return Result<int>.Success(quarantined);
            }

            var candidates = new List<AttendanceRecord>();
            foreach (var (log, utcTime, workDate, recordTz) in normalizedLogs)
            {
                var employeeId = EmployeeId.From(log.EmployeeId);
                var verifyMethod = VerifyMethod.FromValue(log.VerifyMethodCode);
                var checkType = CheckType.FromValue(log.CheckTypeCode);

                var origCheckTime = CivilOf(log, recordTz);

                var record = AttendanceRecord.Create(
                    employeeId,
                    deviceId,
                    utcTime,
                    verifyMethod,
                    checkType,
                    workDate: workDate,
                    originalCheckTime: origCheckTime,
                    timeZoneId: recordTz,
                    source: log.Source ?? command.Source ?? "ADMS");

                if (command.DownloadLogId.HasValue)
                {
                    record.DownloadLogId = new AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLogId(command.DownloadLogId.Value);
                }

                candidates.Add(record);
            }

            // F03-01 / F03-05 / H04: identidad (empleado, instante) sin importar dispositivo; colisiones
            // concurrentes se confirman contra la base de datos y los fallos técnicos no son duplicados.
            var ingestion = new AttendanceRecordIngestion(_attendanceRepository, _unitOfWork, _failureClassifier, _logger);
            var outcome = await ingestion.IngestAsync(candidates, cancellationToken);
            var skippedDuplicates = inBatchDuplicates + outcome.InBatchDuplicates + outcome.ConfirmedDuplicates.Count;

            if (!outcome.IsSuccess)
            {
                _logger.LogError("Lote de {Count} marcajes del dispositivo {DeviceId} no guardado ({Kind}). Duplicados confirmados: {Skipped}.",
                    candidates.Count, command.DeviceId, outcome.Failure!.Kind, skippedDuplicates);
                return Result<int>.Failure(
                    $"No se guardó el lote ({outcome.Failure.Kind}{(outcome.Failure.IsTransient ? ", reintentable" : string.Empty)}): {outcome.Failure.Message}");
            }

            _logger.LogInformation("Procesamiento por lotes completado para dispositivo {DeviceId}. " +
                                  "Agregados: {AddedCount}, Duplicados omitidos: {SkippedCount}",
                                  command.DeviceId, outcome.Inserted.Count, skippedDuplicates);

            return Result<int>.Success(outcome.Inserted.Count + quarantined);
        }
        catch (DomainException ex)
        {
            return Result<int>.Failure(ex.Message);
        }
    }

    /// <summary>Hora civil que marcó el reloj.</summary>
    private DateTime CivilOf(AttendanceLogDto log, string timeZoneId) =>
        log.OriginalCheckTime ?? (log.CheckTime.Kind == DateTimeKind.Utc ? _timeNormalizer.ToCivilTime(log.CheckTime, timeZoneId) : log.CheckTime);

    /// <summary>
    /// Caso 18: un reloj ADMS envió en tiempo real marcajes con hora atrasada (más que la tolerancia de sincronización).
    /// Se le reenvía la hora correcta y, pasado el umbral de alerta, queda la alerta; si el atraso mandó marcajes a
    /// cuarentena (R4), se registra el periodo atrasado y se le piden de nuevo sus marcajes de ese periodo (algunos
    /// firmwares dejan de enviarlos), que pasarán por la cuarentena.
    /// </summary>
    private async Task HandleClockBehindAsync(
        DeviceId deviceId,
        RecordAttendanceBatchCommand command,
        string timeZoneId,
        TimeSpan behind,
        AttendanceSystem.Application.Features.Devices.Quarantine.QuarantineOutcome outcome,
        IReadOnlyList<AttendanceSystem.Application.Features.Devices.Quarantine.PunchCandidate> punches,
        CancellationToken cancellationToken)
    {
        var expectedNow = _quarantine!.ClockNow(timeZoneId);
        var serial = command.DeviceSerial;
        if (_admsCommands != null && serial != null)
        {
            var offset = _timeNormalizer.ResolveTimeZone(timeZoneId).GetUtcOffset(DateTime.UtcNow);
            foreach (var clockCommand in AdmsTimeZone.ClockCommands(expectedNow, offset)) _admsCommands.EnqueueCommand(serial, clockCommand);
        }

        var device = _devices != null ? await _devices.GetByIdAsync(deviceId, cancellationToken) : null;
        var name = device?.Name ?? command.DeviceId;
        var policy = await _quarantine.PolicyAsync(cancellationToken);
        if (behind <= policy.ClockDriftAlert)
        {
            _logger.LogInformation("Reloj ADMS {Device} atrasado {Behind}; se encoló su resincronización sin alerta.", name, behind);
            return;
        }
        if (_alerts != null)
        {
            await _alerts.AddAsync(AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert.Create(AlertType.DeviceClockDrift, deviceId.Value,
                $"El reloj {name} envió en tiempo real marcajes con hora atrasada {AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService.Describe(behind)}; se le reenvió la hora." +
                (outcome.LagQuarantined ? " Sus marcajes de ese periodo quedaron en cuarentena." : "")), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        _logger.LogWarning("ALERTA: el reloj ADMS {Device} está atrasado {Behind}; se encoló su resincronización.", name, behind);

        if (!outcome.LagQuarantined) return;
        var earliest = punches.Min(p => DateTime.SpecifyKind(p.CivilTime, DateTimeKind.Unspecified));
        var from = (outcome.PreviousLatestCivil is { } previous && previous - behind < earliest ? previous - behind : earliest).AddMinutes(-1);
        var to = expectedNow - behind + TimeSpan.FromHours(1);
        await _quarantine.RecordClockIncidentAsync(deviceId, from, to, behind,
            $"En tiempo real, el reloj marcaba {expectedNow - behind:dd/MM/yyyy HH:mm} cuando eran las {expectedNow:dd/MM/yyyy HH:mm}", cancellationToken);

        if (_admsCommands != null && serial != null)
        {
            var table = device?.DeviceType?.Equals("acc", StringComparison.OrdinalIgnoreCase) == true ? "Transaction" : "ATTLOG";
            _admsCommands.EnqueueCommand(serial, FormattableString.Invariant($"DATA QUERY tablename={table},starttime={from:yyyy-MM-dd HH:mm:ss},endtime={to:yyyy-MM-dd HH:mm:ss}"));
        }
    }
}
