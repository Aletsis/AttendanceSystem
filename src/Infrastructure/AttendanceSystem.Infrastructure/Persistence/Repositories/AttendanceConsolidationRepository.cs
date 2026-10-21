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

    public Task RequestManyAsync(IReadOnlyCollection<EmployeeId> employeeIds, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) =>
        RequestRangesAsync(employeeIds.Select(e => (e, startDate, endDate)).ToList(), cancellationToken);

    public async Task RequestRangesAsync(
        IReadOnlyCollection<(EmployeeId EmployeeId, DateTime StartDate, DateTime EndDate)> ranges,
        CancellationToken cancellationToken = default)
    {
        if (ranges.Count == 0) return;

        // Un rango por empleado (unión) y en orden de empleado: ON CONFLICT no puede tocar la misma fila dos veces
        // y el orden fijo evita interbloqueos entre procesos que amplían a la vez.
        var merged = ranges
            .Select(r => (Id: r.EmployeeId.Value,
                          Start: DateOnly.FromDateTime(r.StartDate.Date <= r.EndDate.Date ? r.StartDate : r.EndDate),
                          End: DateOnly.FromDateTime(r.StartDate.Date <= r.EndDate.Date ? r.EndDate : r.StartDate)))
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .Select(g => (Id: g.Key, Start: g.Min(r => r.Start), End: g.Max(r => r.End)))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        // F07-04 / C01: una sola sentencia para todo el lote (antes una por empleado). Cada empleado: absorbe sus
        // fallidas y registra o amplía su solicitud activa (unión de rangos, Version+1), igual que la versión individual.
        foreach (var chunk in merged.Chunk(1000))
        {
            await _context.Database.ExecuteSqlRawAsync($@"
WITH input AS (
    SELECT * FROM unnest({{0}}::text[], {{1}}::date[], {{2}}::date[], {{3}}::uuid[])
             AS i(""EmployeeId"", ""StartDate"", ""EndDate"", ""Id"")),
absorbed AS (
    UPDATE {Table} t
       SET ""Status"" = 4, ""ProcessedAt"" = now(),
           ""ErrorMessage"" = left(coalesce(t.""ErrorMessage"", '') || ' [absorbida por una nueva solicitud]', 2000)
      FROM input i
     WHERE t.""EmployeeId"" = i.""EmployeeId"" AND t.""Status"" = 3
 RETURNING t.""EmployeeId"", t.""StartDate"", t.""EndDate""),
merged AS (
    SELECT i.""Id"", i.""EmployeeId"",
           LEAST(i.""StartDate"", (SELECT min(a.""StartDate"") FROM absorbed a WHERE a.""EmployeeId"" = i.""EmployeeId"")) AS ""StartDate"",
           GREATEST(i.""EndDate"", (SELECT max(a.""EndDate"") FROM absorbed a WHERE a.""EmployeeId"" = i.""EmployeeId"")) AS ""EndDate""
      FROM input i)
INSERT INTO {Table} AS r
       (""Id"", ""EmployeeId"", ""StartDate"", ""EndDate"", ""Status"", ""RequestedAt"", ""LastRequestedAt"", ""Version"", ""RetryCount"")
SELECT m.""Id"", m.""EmployeeId"", m.""StartDate"", m.""EndDate"", 0, now(), now(), 1, 0
  FROM merged m
 ORDER BY m.""EmployeeId""
ON CONFLICT (""EmployeeId"") WHERE ""Status"" IN (0, 1)
DO UPDATE SET ""StartDate"" = LEAST(r.""StartDate"", EXCLUDED.""StartDate""),
              ""EndDate"" = GREATEST(r.""EndDate"", EXCLUDED.""EndDate""),
              ""Version"" = r.""Version"" + 1,
              ""LastRequestedAt"" = now()",
                new object[]
                {
                    chunk.Select(r => r.Id).ToArray(),
                    chunk.Select(r => r.Start).ToArray(),
                    chunk.Select(r => r.End).ToArray(),
                    chunk.Select(_ => Guid.NewGuid()).ToArray()
                },
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

    public async Task<IReadOnlyDictionary<Guid, ConsolidationCompletion>> CompleteManyAsync(
        IReadOnlyCollection<(Guid RequestId, int ClaimedVersion)> claims, string consumerId, CancellationToken cancellationToken = default)
    {
        var result = claims.ToDictionary(c => c.RequestId, _ => ConsolidationCompletion.LeaseLost);
        if (claims.Count == 0) return result;

        // Mismas reglas que CompleteAsync, en una sentencia (una confirmación): completar sólo la versión procesada; si
        // se amplió durante el cálculo vuelve a Pending con el rango acumulado; sin lease del consumidor no se toca.
        var sql = $@"
UPDATE {Table} r
   SET ""Status"" = CASE WHEN r.""Version"" = i.version THEN 2 ELSE 0 END,
       ""ProcessedAt"" = CASE WHEN r.""Version"" = i.version THEN now() ELSE r.""ProcessedAt"" END,
       ""RetryCount"" = CASE WHEN r.""Version"" = i.version THEN 0 ELSE r.""RetryCount"" END,
       ""ErrorMessage"" = NULL, ""NextAttemptAt"" = NULL,
       ""ClaimedBy"" = NULL, ""ClaimedVersion"" = NULL, ""LeaseExpiresAt"" = NULL
  FROM unnest(@ids, @versions) AS i(id, version)
 WHERE r.""Id"" = i.id AND r.""Status"" = 1 AND r.""ClaimedBy"" = @consumer
RETURNING r.""Id"", r.""Status""";

        await using var command = await CreateCommandAsync(sql, cancellationToken,
            ("ids", claims.Select(c => c.RequestId).ToArray()),
            ("versions", claims.Select(c => c.ClaimedVersion).ToArray()),
            ("consumer", consumerId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetGuid(0)] = reader.GetInt32(1) == (int)ConsolidationStatus.Completed
                ? ConsolidationCompletion.Completed
                : ConsolidationCompletion.Requeued;
        }
        return result;
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
