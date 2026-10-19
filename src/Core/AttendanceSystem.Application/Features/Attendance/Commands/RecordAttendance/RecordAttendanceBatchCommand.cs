using System.Collections.Generic;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Primitives;
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
    string? Source = null
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
        ILogger<RecordAttendanceBatchCommandHandler>? logger = null)
    {
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

            if (normalizedLogs.Count == 0)
            {
                return Result<int>.Success(0);
            }

            var minDate = normalizedLogs.Min(l => l.UtcTime);
            var maxDate = normalizedLogs.Max(l => l.UtcTime);

            // 2. Recuperar registros existentes en el rango de tiempo del lote para este dispositivo
            var existingRecords = await _attendanceRepository.GetByDeviceAndDateRangeAsync(
                deviceId,
                minDate,
                maxDate,
                cancellationToken);

            var existingKeys = existingRecords
                .Select(r => (r.EmployeeId.Value, r.CheckTime))
                .ToHashSet();

            var newRecords = new List<AttendanceRecord>();
            int skippedDuplicates = inBatchDuplicates;

            foreach (var (log, utcTime, workDate, recordTz) in normalizedLogs)
            {
                if (existingKeys.Contains((log.EmployeeId, utcTime)))
                {
                    skippedDuplicates++;
                    continue;
                }

                var employeeId = EmployeeId.From(log.EmployeeId);
                var verifyMethod = VerifyMethod.FromValue(log.VerifyMethodCode);
                var checkType = CheckType.FromValue(log.CheckTypeCode);

                var origCheckTime = log.OriginalCheckTime ?? (log.CheckTime.Kind == DateTimeKind.Utc ? _timeNormalizer.ToCivilTime(log.CheckTime, recordTz) : log.CheckTime);

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

                newRecords.Add(record);
            }

            if (newRecords.Count > 0)
            {
                try
                {
                    await _attendanceRepository.AddRangeAsync(newRecords, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Conflicto de concurrencia al guardar lote para dispositivo {DeviceId}. Intentando reconciliación fila a fila.",
                        command.DeviceId);

                    // Reintentar fila a fila para descartar las colisiones concurrentes y guardar el resto
                    int addedIndividually = 0;
                    foreach (var record in newRecords)
                    {
                        var alreadyExists = await _attendanceRepository.GetByEmployeeAndCheckTimeAsync(
                            record.EmployeeId,
                            record.CheckTime,
                            cancellationToken);

                        if (alreadyExists != null)
                        {
                            skippedDuplicates++;
                            continue;
                        }

                        try
                        {
                            await _attendanceRepository.AddAsync(record, cancellationToken);
                            await _unitOfWork.SaveChangesAsync(cancellationToken);
                            addedIndividually++;
                        }
                        catch (Exception)
                        {
                            skippedDuplicates++;
                        }
                    }

                    _logger.LogInformation("Reconciliación de lote completada para dispositivo {DeviceId}. Agregados: {AddedCount}, Duplicados omitidos: {SkippedCount}",
                        command.DeviceId, addedIndividually, skippedDuplicates);

                    return Result<int>.Success(addedIndividually);
                }
            }

            _logger.LogInformation("Procesamiento por lotes completado para dispositivo {DeviceId}. " +
                                  "Agregados: {AddedCount}, Duplicados omitidos: {SkippedCount}",
                                  command.DeviceId, newRecords.Count, skippedDuplicates);

            return Result<int>.Success(newRecords.Count);
        }
        catch (DomainException ex)
        {
            return Result<int>.Failure(ex.Message);
        }
    }
}
