using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

/// <summary>
/// Transiciones atómicas de <c>AttendanceConsolidationRequests</c> en PostgreSQL (F04-04 / F04-05 / F04-06).
/// Las horas de lease, espera y backoff usan <c>now()</c> del servidor para no depender del reloj de cada proceso.
/// </summary>
public class AttendanceConsolidationRepository : IAttendanceConsolidationRepository
{
    private const string Table = "\"AttendanceConsolidationRequests\"";

    private readonly AttendanceDbContext _context;

    public AttendanceConsolidationRepository(AttendanceDbContext context)
    {
        _context = context;
    }

    public Task RequestAsync(EmployeeId employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) =>
        RequestManyAsync(new[] { employeeId }, startDate, endDate, cancellationToken);

    public async Task RequestManyAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0) return;

        // DateOnly se envía como date: sin conversión de zona horaria de la sesión.
        var start = DateOnly.FromDateTime(startDate.Date <= endDate.Date ? startDate : endDate);
        var end = DateOnly.FromDateTime(startDate.Date <= endDate.Date ? endDate : startDate);

        // Un solo INSERT ... ON CONFLICT por empleado y en orden: ampliar es atómico (unión de rangos y
        // Version+1) aunque otro proceso amplíe a la vez o la solicitud esté siendo procesada. Las fallidas
        // del empleado se absorben en la activa para que la nueva actividad las reintente.
        foreach (var employeeId in employeeIds.Select(e => e.Value).Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal))
        {
            await _context.Database.ExecuteSqlRawAsync($@"
WITH absorbed AS (
    UPDATE {Table}
       SET ""Status"" = 4, ""ProcessedAt"" = now(),
           ""ErrorMessage"" = left(coalesce(""ErrorMessage"", '') || ' [absorbida por una nueva solicitud]', 2000)
     WHERE ""EmployeeId"" = {{0}} AND ""Status"" = 3
 RETURNING ""StartDate"", ""EndDate"")
INSERT INTO {Table} AS r
       (""Id"", ""EmployeeId"", ""StartDate"", ""EndDate"", ""Status"", ""RequestedAt"", ""LastRequestedAt"", ""Version"", ""RetryCount"")
SELECT {{1}}, {{0}},
       LEAST({{2}}, (SELECT min(""StartDate"") FROM absorbed)),
       GREATEST({{3}}, (SELECT max(""EndDate"") FROM absorbed)),
       0, now(), now(), 1, 0
ON CONFLICT (""EmployeeId"") WHERE ""Status"" IN (0, 1)
DO UPDATE SET ""StartDate"" = LEAST(r.""StartDate"", EXCLUDED.""StartDate""),
              ""EndDate"" = GREATEST(r.""EndDate"", EXCLUDED.""EndDate""),
              ""Version"" = r.""Version"" + 1,
              ""LastRequestedAt"" = now()",
                new object[] { employeeId, Guid.NewGuid(), start, end },
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ConsolidationClaim>> ClaimAsync(
        string consumerId,
        int maxBatchSize,
        TimeSpan lease,
        TimeSpan quietPeriod,
        TimeSpan maxDelay,
        CancellationToken cancellationToken = default)
    {
        // FOR UPDATE SKIP LOCKED: dos consumidores reclaman conjuntos disjuntos sin esperarse.
        var sql = $@"
UPDATE {Table} r
   SET ""Status"" = 1, ""ClaimedBy"" = @consumer, ""ClaimedVersion"" = r.""Version"",
       ""LeaseExpiresAt"" = now() + make_interval(secs => @lease)
  FROM (SELECT ""Id"" FROM {Table}
         WHERE (""Status"" = 0
                AND (""NextAttemptAt"" IS NULL OR ""NextAttemptAt"" <= now())
                AND (""LastRequestedAt"" <= now() - make_interval(secs => @quiet)
                     OR ""RequestedAt"" <= now() - make_interval(secs => @maxDelay)))
            OR (""Status"" = 1 AND ""LeaseExpiresAt"" < now())
         ORDER BY ""RequestedAt"", ""Id""
         LIMIT @limit
         FOR UPDATE SKIP LOCKED) c
 WHERE r.""Id"" = c.""Id""
RETURNING r.""Id"", r.""EmployeeId"", r.""StartDate"", r.""EndDate"", r.""Version"", r.""RetryCount""";

        var claims = new List<ConsolidationClaim>();
        await using var command = await CreateCommandAsync(sql, cancellationToken,
            ("consumer", consumerId), ("lease", lease.TotalSeconds), ("quiet", quietPeriod.TotalSeconds),
            ("maxDelay", maxDelay.TotalSeconds), ("limit", maxBatchSize));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claims.Add(new ConsolidationClaim(
                reader.GetGuid(0),
                EmployeeId.From(reader.GetString(1)),
                reader.GetDateTime(2),
                reader.GetDateTime(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return claims.OrderBy(c => c.EmployeeId.Value, StringComparer.Ordinal).ToList();
    }

    public async Task<ConsolidationCompletion> CompleteAsync(Guid requestId, string consumerId, int claimedVersion, CancellationToken cancellationToken = default)
    {
        // Completar sólo la versión procesada; si se amplió durante el cálculo vuelve a Pending con el rango acumulado.
        var sql = $@"
UPDATE {Table}
   SET ""Status"" = CASE WHEN ""Version"" = @version THEN 2 ELSE 0 END,
       ""ProcessedAt"" = CASE WHEN ""Version"" = @version THEN now() ELSE ""ProcessedAt"" END,
       ""RetryCount"" = CASE WHEN ""Version"" = @version THEN 0 ELSE ""RetryCount"" END,
       ""ErrorMessage"" = NULL, ""NextAttemptAt"" = NULL,
       ""ClaimedBy"" = NULL, ""ClaimedVersion"" = NULL, ""LeaseExpiresAt"" = NULL
 WHERE ""Id"" = @id AND ""Status"" = 1 AND ""ClaimedBy"" = @consumer
RETURNING ""Status""";

        await using var command = await CreateCommandAsync(sql, cancellationToken, ("id", requestId), ("consumer", consumerId), ("version", claimedVersion));
        var status = await command.ExecuteScalarAsync(cancellationToken);
        return status switch
        {
            null or DBNull => ConsolidationCompletion.LeaseLost,
            int s when s == (int)ConsolidationStatus.Completed => ConsolidationCompletion.Completed,
            _ => ConsolidationCompletion.Requeued
        };
    }

    public async Task FailAsync(Guid requestId, string consumerId, string error, int maxAttempts, TimeSpan baseBackoff, CancellationToken cancellationToken = default)
    {
        var sql = $@"
UPDATE {Table}
   SET ""RetryCount"" = ""RetryCount"" + 1,
       ""ErrorMessage"" = left(@error, 2000),
       ""Status"" = CASE WHEN ""RetryCount"" + 1 >= @max THEN 3 ELSE 0 END,
       ""ProcessedAt"" = CASE WHEN ""RetryCount"" + 1 >= @max THEN now() ELSE ""ProcessedAt"" END,
       ""NextAttemptAt"" = CASE WHEN ""RetryCount"" + 1 >= @max THEN NULL
                              ELSE now() + make_interval(secs => LEAST(3600, @backoff * power(2, ""RetryCount""))) END,
       ""ClaimedBy"" = NULL, ""ClaimedVersion"" = NULL, ""LeaseExpiresAt"" = NULL
 WHERE ""Id"" = @id AND ""Status"" = 1 AND ""ClaimedBy"" = @consumer";

        await using var command = await CreateCommandAsync(sql, cancellationToken,
            ("id", requestId), ("consumer", consumerId), ("error", error), ("max", maxAttempts), ("backoff", baseBackoff.TotalSeconds));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid requestId, string consumerId, CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync($@"
UPDATE {Table}
   SET ""Status"" = 0, ""ClaimedBy"" = NULL, ""ClaimedVersion"" = NULL, ""LeaseExpiresAt"" = NULL
 WHERE ""Id"" = {{0}} AND ""Status"" = 1 AND ""ClaimedBy"" = {{1}}",
            new object[] { requestId, consumerId },
            cancellationToken);
    }

    public async Task<int> RetryFailedAsync(Guid? requestId = null, CancellationToken cancellationToken = default)
    {
        var failed = await _context.AttendanceConsolidationRequests
            .AsNoTracking()
            .Where(x => x.Status == ConsolidationStatus.Failed && (requestId == null || x.Id == requestId))
            .Select(x => new { x.EmployeeId, x.StartDate, x.EndDate })
            .ToListAsync(cancellationToken);

        // Reintentar es volver a solicitar: la fallida se absorbe en la activa del empleado (o la crea).
        foreach (var request in failed.GroupBy(f => f.EmployeeId.Value, StringComparer.Ordinal))
        {
            await RequestAsync(EmployeeId.From(request.Key), request.Min(r => r.StartDate), request.Max(r => r.EndDate), cancellationToken);
        }

        return failed.Count;
    }

    public async Task<AttendanceConsolidationRequest?> GetActiveByEmployeeAsync(EmployeeId employeeId, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceConsolidationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId &&
                (x.Status == ConsolidationStatus.Pending || x.Status == ConsolidationStatus.Processing), cancellationToken);
    }

    public async Task<ConsolidationHealth> GetHealthAsync(int recentFailures = 20, CancellationToken cancellationToken = default)
    {
        var requests = _context.AttendanceConsolidationRequests.AsNoTracking();
        var counts = await requests
            .Where(x => x.Status == ConsolidationStatus.Pending || x.Status == ConsolidationStatus.Processing || x.Status == ConsolidationStatus.Failed)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Oldest = g.Min(x => x.RequestedAt), MaxAttempts = g.Max(x => x.RetryCount) })
            .ToListAsync(cancellationToken);

        var failures = await requests
            .Where(x => x.Status == ConsolidationStatus.Failed)
            .OrderByDescending(x => x.ProcessedAt)
            .Take(recentFailures)
            .ToListAsync(cancellationToken);

        int Count(ConsolidationStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        var active = counts.Where(c => c.Status is ConsolidationStatus.Pending or ConsolidationStatus.Processing).ToList();

        return new ConsolidationHealth(
            Count(ConsolidationStatus.Pending),
            Count(ConsolidationStatus.Processing),
            Count(ConsolidationStatus.Failed),
            active.Count == 0 ? null : active.Min(c => c.Oldest),
            active.Count == 0 ? 0 : active.Max(c => c.MaxAttempts),
            failures);
    }

    public async Task<int> PurgeFinishedAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        return await _context.Database.ExecuteSqlRawAsync($@"
DELETE FROM {Table}
 WHERE ""Status"" IN (2, 4) AND ""ProcessedAt"" < now() - make_interval(secs => {{0}})",
            new object[] { retention.TotalSeconds },
            cancellationToken);
    }

    private async Task<DbCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await _context.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
