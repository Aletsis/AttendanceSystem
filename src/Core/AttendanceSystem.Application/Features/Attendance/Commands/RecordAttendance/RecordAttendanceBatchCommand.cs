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
    Guid? DownloadLogId = null
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
        AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService? quarantine = null)
    {
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
                var impossible = await _quarantine.QuarantineImpossibleAsync(deviceId, command.DeviceId, punches, cancellationToken);
                if (impossible.Count > 0)
                {
                    quarantined = impossible.Count;
                    normalizedLogs = normalizedLogs.Where((_, i) => !impossible.Contains(i)).ToList();
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
}
