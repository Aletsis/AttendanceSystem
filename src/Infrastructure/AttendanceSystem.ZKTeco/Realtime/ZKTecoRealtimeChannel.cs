using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttendanceSystem.ZKTeco.Realtime;

/// <summary>
/// Cola de eventos en tiempo real hacia un único suscriptor. Guarda los eventos mientras nadie lee (hasta su capacidad,
/// luego descarta los más viejos: los recupera la descarga de respaldo). Un suscriptor nuevo reemplaza al anterior, para
/// que nunca se procese un marcaje dos veces.
/// </summary>
public sealed class ZKTecoRealtimeChannel
{
    private readonly Channel<RealtimeEvent> _channel;
    private readonly ILogger<ZKTecoRealtimeChannel> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _subscriber;

    public ZKTecoRealtimeChannel(IOptions<ZKTecoRealtimeOptions> options, ILogger<ZKTecoRealtimeChannel> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<RealtimeEvent>(
            new BoundedChannelOptions(Math.Max(1, options.Value.ChannelCapacity)) { FullMode = BoundedChannelFullMode.DropOldest },
            dropped => _logger.LogWarning("Cola de tiempo real llena: se descartó un evento del reloj {DeviceId} del {OccurredAt:u}.",
                dropped.DeviceId, dropped.OccurredAtUtc));
    }

    public void Publish(RealtimeEvent realtimeEvent) => _channel.Writer.TryWrite(realtimeEvent);

    public async IAsyncEnumerable<RealtimeEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CancellationTokenSource mine;
        lock (_gate)
        {
            if (_subscriber != null)
            {
                _logger.LogWarning("Un suscriptor nuevo de tiempo real reemplaza al anterior.");
                _subscriber.Cancel();
            }
            mine = _subscriber = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        try
        {
            while (await WaitToReadAsync(mine.Token))
            {
                while (!mine.IsCancellationRequested && _channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_subscriber == mine) _subscriber = null;
                mine.Dispose();
            }
        }
    }

    private async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _channel.Reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
