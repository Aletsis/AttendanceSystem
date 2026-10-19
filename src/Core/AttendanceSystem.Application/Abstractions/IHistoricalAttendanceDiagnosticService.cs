namespace AttendanceSystem.Application.Abstractions;

public sealed record HistoricalDiagnosticReport(
    int TotalInspected,
    int ReliableRecordsCount,
    int MissingWorkDateCount,
    int MissingOriginalCheckTimeCount,
    int MissingTimeZoneCount,
    int PotentialCollisionsCount,
    int PendingModificationsCount,
    IReadOnlyList<string> AnomaliesRequiringHumanReview);

public sealed record HistoricalMigrationResult(
    int TotalProcessed,
    int UpdatedRecordsCount,
    int UnchangedRecordsCount,
    int CollisionSkippedCount,
    bool IsDryRun);

public interface IHistoricalAttendanceDiagnosticService
{
    Task<HistoricalDiagnosticReport> DiagnoseAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default);

    Task<HistoricalMigrationResult> MigrateAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
