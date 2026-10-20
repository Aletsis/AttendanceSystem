using System.Data;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Outbox;

/// <summary>
/// Protocolo único del outbox (F04-01 / F04-02 / F04-03), usado por la ruta inmediata del
/// <see cref="AttendanceDbContext"/> y por <see cref="Services.OutboxBackgroundProcessor"/>:
/// <list type="number">
/// <item>Un mensaje se publica sólo si quien lo publica lo tiene reclamado (LockedBy / LockedUntilUtc).</item>
/// <item>La ruta inmediata guarda sus mensajes ya reclamados; el worker sólo reclama los libres o con lease
/// vencido, con <c>FOR UPDATE SKIP LOCKED</c>. Así no compiten por el mismo mensaje.</item>
/// <item>Éxito → procesado. Fallo → intento con backoff o mensaje muerto. Cancelación → liberado sin intento.</item>
/// </list>
/// La entrega es al menos una vez (una caída entre publicar y marcar repite la publicación tras el lease):
/// los consumidores son idempotentes.
/// </summary>
public sealed class OutboxStore : IOutboxMonitor
{
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(2);

    private readonly AttendanceDbContext _context;

    public OutboxStore(AttendanceDbContext context)
    {
        _context = context;
    }

    /// <summary>Reclama hasta <paramref name="limit"/> mensajes disponibles y los devuelve rastreados.</summary>
    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(string consumerId, int limit, TimeSpan lease, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await _context.Database.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE ""OutboxMessages"" m
   SET ""LockedBy"" = @consumer, ""LockedUntilUtc"" = now() + make_interval(secs => @lease)
  FROM (SELECT ""Id"" FROM ""OutboxMessages""
         WHERE ""ProcessedOnUtc"" IS NULL AND ""DeadLetteredOnUtc"" IS NULL
           AND (""LockedUntilUtc"" IS NULL OR ""LockedUntilUtc"" < now())
           AND (""NextAttemptOnUtc"" IS NULL OR ""NextAttemptOnUtc"" <= now())
         ORDER BY ""OccurredOnUtc"", ""Id""
         LIMIT @limit
         FOR UPDATE SKIP LOCKED) c
 WHERE m.""Id"" = c.""Id""
RETURNING m.""Id""";
        AddParameter(command, "consumer", consumerId);
        AddParameter(command, "lease", lease.TotalSeconds);
        AddParameter(command, "limit", limit);

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }

        if (ids.Count == 0) return Array.Empty<OutboxMessage>();

        var messages = await _context.OutboxMessages.Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            // La reclamación se hizo en SQL; se refleja en la entidad para no sobrescribirla al guardar.
            await _context.Entry(message).ReloadAsync(cancellationToken);
        }
        return messages.OrderBy(m => m.OccurredOnUtc).ThenBy(m => m.Id).ToList();
    }

    /// <summary>Deserializa y publica un mensaje. Lanza si el tipo no se resuelve o algún manejador falla.</summary>
    public static async Task PublishAsync(OutboxMessage message, IPublisher publisher, CancellationToken cancellationToken)
    {
        var eventType = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"No se pudo resolver el tipo '{message.Type}'.");

        if (DomainEventJson.Deserialize(message.Content, eventType) is not INotification notification)
        {
            throw new InvalidOperationException("El evento deserializado no implementa INotification.");
        }

        await publisher.Publish(notification, cancellationToken);
    }

    /// <summary>Publica mensajes ya reclamados y registra el resultado de cada uno en la entidad.</summary>
    public static async Task PublishClaimedAsync(
        IReadOnlyList<(OutboxMessage Message, INotification? Event)> claimed,
        IPublisher publisher,
        CancellationToken cancellationToken)
    {
        foreach (var (message, domainEvent) in claimed)
        {
            try
            {
                if (domainEvent != null) await publisher.Publish(domainEvent, cancellationToken);
                else await PublishAsync(message, publisher, cancellationToken);
                message.MarkAsProcessed();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                message.Release();
            }
            catch (Exception ex)
            {
                message.MarkAsFailed($"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public async Task<OutboxHealth> GetHealthAsync(int recentDeadLetters = 20, CancellationToken cancellationToken = default)
    {
        var messages = _context.OutboxMessages.AsNoTracking();
        var now = DateTime.UtcNow;
        var pending = messages.Where(m => m.ProcessedOnUtc == null && m.DeadLetteredOnUtc == null);

        var pendingCount = await pending.CountAsync(m => m.LockedUntilUtc == null || m.LockedUntilUtc < now, cancellationToken);
        var inFlight = await pending.CountAsync(m => m.LockedUntilUtc != null && m.LockedUntilUtc >= now, cancellationToken);
        var oldest = await pending.OrderBy(m => m.OccurredOnUtc).Select(m => (DateTime?)m.OccurredOnUtc).FirstOrDefaultAsync(cancellationToken);
        var deadCount = await messages.CountAsync(m => m.DeadLetteredOnUtc != null && m.ProcessedOnUtc == null, cancellationToken);
        var dead = await messages
            .Where(m => m.DeadLetteredOnUtc != null && m.ProcessedOnUtc == null)
            .OrderByDescending(m => m.DeadLetteredOnUtc)
            .Take(recentDeadLetters)
            .Select(m => new OutboxFailure(m.Id, m.Type, m.OccurredOnUtc, m.RetryCount, m.Error))
            .ToListAsync(cancellationToken);

        return new OutboxHealth(pendingCount, inFlight, deadCount, oldest, dead);
    }

    public async Task<int> ReviveDeadLettersAsync(Guid? messageId = null, CancellationToken cancellationToken = default)
    {
        var dead = await _context.OutboxMessages
            .Where(m => m.DeadLetteredOnUtc != null && m.ProcessedOnUtc == null && (messageId == null || m.Id == messageId))
            .ToListAsync(cancellationToken);

        foreach (var message in dead) message.Revive();
        await _context.SaveChangesAsync(cancellationToken);
        return dead.Count;
    }

    public async Task<int> PurgeProcessedAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        return await _context.Database.ExecuteSqlRawAsync(
            @"DELETE FROM ""OutboxMessages"" WHERE ""ProcessedOnUtc"" < now() - make_interval(secs => {0})",
            new object[] { retention.TotalSeconds },
            cancellationToken);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
