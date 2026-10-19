using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Services;

public class HistoricalAttendanceTimeDiagnosticService : IHistoricalAttendanceDiagnosticService
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<HistoricalAttendanceTimeDiagnosticService> _logger;

    public HistoricalAttendanceTimeDiagnosticService(
        IAttendanceRepository attendanceRepository,
        IAuthoritativeTimeZoneResolver timeZoneResolver,
        ITimeNormalizer timeNormalizer,
        IUnitOfWork unitOfWork,
        ILogger<HistoricalAttendanceTimeDiagnosticService> logger)
    {
        _attendanceRepository = attendanceRepository;
        _timeZoneResolver = timeZoneResolver;
        _timeNormalizer = timeNormalizer;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<HistoricalDiagnosticReport> DiagnoseAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var records = await GetFilteredRecordsAsync(fromDate, toDate, cancellationToken);

        int total = records.Count;
        int reliable = 0;
        int missingWorkDate = 0;
        int missingOriginal = 0;
        int missingTz = 0;
        int pendingModifications = 0;
        var anomalies = new List<string>();

        foreach (var r in records)
        {
            bool isReliable = r.WorkDate.HasValue && r.OriginalCheckTime.HasValue && !string.IsNullOrWhiteSpace(r.TimeZoneId);
            if (isReliable)
            {
                reliable++;
            }
            else
            {
                pendingModifications++;
            }

            if (!r.WorkDate.HasValue) missingWorkDate++;
            if (!r.OriginalCheckTime.HasValue) missingOriginal++;
            if (string.IsNullOrWhiteSpace(r.TimeZoneId)) missingTz++;

            // Detect potential ambiguities or anomalies
            if (r.CheckTime.Kind == DateTimeKind.Unspecified && !r.OriginalCheckTime.HasValue)
            {
                anomalies.Add($"Registro {r.Id.Value} (Empleado {r.EmployeeId.Value}) tiene Kind=Unspecified sin OriginalCheckTime.");
            }
        }

        _logger.LogInformation(
            "Diagnóstico de tiempos completado. Total: {Total}, Fiables: {Reliable}, Pendientes: {Pending}",
            total, reliable, pendingModifications);

        return new HistoricalDiagnosticReport(
            TotalInspected: total,
            ReliableRecordsCount: reliable,
            MissingWorkDateCount: missingWorkDate,
            MissingOriginalCheckTimeCount: missingOriginal,
            MissingTimeZoneCount: missingTz,
            PotentialCollisionsCount: 0,
            PendingModificationsCount: pendingModifications,
            AnomaliesRequiringHumanReview: anomalies);
    }

    public async Task<HistoricalMigrationResult> MigrateAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var records = await GetFilteredRecordsAsync(fromDate, toDate, cancellationToken);
        var deviceTzCache = new Dictionary<string, string>();

        int updatedCount = 0;
        int unchangedCount = 0;

        foreach (var r in records)
        {
            bool needsWorkDate = !r.WorkDate.HasValue;
            bool needsOriginal = !r.OriginalCheckTime.HasValue;
            bool needsTz = string.IsNullOrWhiteSpace(r.TimeZoneId);

            if (!needsWorkDate && !needsOriginal && !needsTz)
            {
                unchangedCount++;
                continue;
            }

            var deviceKey = r.DeviceId?.Value ?? string.Empty;
            if (!deviceTzCache.TryGetValue(deviceKey, out var authoritativeTz))
            {
                authoritativeTz = await _timeZoneResolver.ResolveForDeviceAsync(r.DeviceId, cancellationToken);
                deviceTzCache[deviceKey] = authoritativeTz;
            }

            var civilTime = r.OriginalCheckTime ?? _timeNormalizer.ToCivilTime(r.CheckTime, authoritativeTz);
            var workDate = r.WorkDate ?? _timeNormalizer.ToWorkDate(r.CheckTime, authoritativeTz);
            var timeZoneId = !string.IsNullOrWhiteSpace(r.TimeZoneId) ? r.TimeZoneId : authoritativeTz;

            if (!dryRun)
            {
                r.UpdateTimeMetadata(civilTime, workDate, timeZoneId);
                await _attendanceRepository.UpdateAsync(r, cancellationToken);
            }

            updatedCount++;
        }

        if (!dryRun && updatedCount > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Migración histórica de tiempos completada (DryRun={DryRun}). Procesados: {Total}, Actualizados: {Updated}, Sin cambios: {Unchanged}",
            dryRun, records.Count, updatedCount, unchangedCount);

        return new HistoricalMigrationResult(
            TotalProcessed: records.Count,
            UpdatedRecordsCount: updatedCount,
            UnchangedRecordsCount: unchangedCount,
            CollisionSkippedCount: 0,
            IsDryRun: dryRun);
    }

    private async Task<IReadOnlyList<AttendanceRecord>> GetFilteredRecordsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        if (fromDate.HasValue && toDate.HasValue)
        {
            return await _attendanceRepository.GetByDateRangeAsync(fromDate.Value, toDate.Value, null, cancellationToken);
        }

        return await _attendanceRepository.GetAllAsync(cancellationToken);
    }
}
