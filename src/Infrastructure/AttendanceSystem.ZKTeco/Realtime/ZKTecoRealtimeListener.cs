using AttendanceSystem.ZKTeco.Adapters;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.ZKTeco.Realtime;

/// <summary>
/// Mantiene abierta la conexión SDK de un reloj y publica sus marcajes en tiempo real. La conexión es la "sesión fija"
/// del reloj: las demás operaciones la toman prestada con el candado de su IP en vez de abrir otra.
/// <para>
/// El SDK no avisa cuando el reloj se cae (OnDisConnected no llega): un latido lee la hora del reloj y, si no responde
/// (tarda ~46 s en fallar), se desconecta y se reintenta con espera creciente. El latido se omite mientras una sesión
/// tiene prestada la conexión, para no retenerla.
/// </para>
/// </summary>
internal sealed class ZKTecoRealtimeListener : IAsyncDisposable
{
    private const int RecentPunchCapacity = 256;

    private readonly IZKTecoClient _client;
    private readonly SemaphoreSlim _ipLock;
    private readonly ZKTecoRealtimeChannel _channel;
    private readonly ZKTecoRealtimeOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _recentGate = new();
    private readonly HashSet<(string UserId, DateTime CheckTime)> _recent = new();
    private readonly Queue<(string UserId, DateTime CheckTime)> _recentOrder = new();
    private Task? _loop;
    private volatile bool _connected;
    private long _lastAliveTicks;
    private int _disposed;

    public ZKTecoRealtimeListener(
        RealtimeDeviceTarget target,
        IZKTecoClient client,
        SemaphoreSlim ipLock,
        ZKTecoRealtimeChannel channel,
        ZKTecoRealtimeOptions options,
        ILogger logger)
    {
        Target = target;
        _client = client;
        _ipLock = ipLock;
        _channel = channel;
        _options = options;
        _logger = logger;
        _client.PunchReceived += OnPunch;
    }

    public RealtimeDeviceTarget Target { get; }

    public IZKTecoClient Client => _client;

    public bool IsConnected => _connected;

    public DateTime? LastAliveUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastAliveTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>
    /// Para una sesión que toma prestada la conexión, con el candado de la IP ya tomado: reconecta si estaba caída.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken) =>
        _connected || await ConnectCoreAsync(cancellationToken);

    private async Task RunAsync(CancellationToken stop)
    {
        var failedAttempts = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!_connected)
                {
                    await _ipLock.WaitAsync(stop);
                    bool connected;
                    try
                    {
                        // Una sesión prestada pudo reconectarla mientras se esperaba el candado.
                        connected = _connected || await ConnectCoreAsync(stop);
                    }
                    finally
                    {
                        _ipLock.Release();
                    }

                    if (!connected)
                    {
                        await Task.Delay(ReconnectDelay(failedAttempts++), stop);
                        continue;
                    }
                    failedAttempts = 0;
                }

                await Task.Delay(_options.HeartbeatInterval, stop);

                // Con la conexión prestada, las operaciones de la sesión ya la prueban: no se retiene con el latido.
                if (!await _ipLock.WaitAsync(0, stop)) continue;
                try
                {
                    if (_connected && !await HeartbeatAsync(stop))
                    {
                        await MarkDisconnectedAsync("El reloj no respondió al latido");
                    }
                }
                finally
                {
                    _ipLock.Release();
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en la escucha en tiempo real de {IpKey}", Target.IpKey);
                try
                {
                    await Task.Delay(ReconnectDelay(failedAttempts), stop);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<bool> ConnectCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _client.ConnectAsync(Target.IpAddress, Target.Port, cancellationToken: cancellationToken))
            {
                _logger.LogWarning("Tiempo real: no se pudo conectar a {IpKey}.", Target.IpKey);
                return false;
            }

            if (!await _client.EnableRealtimeEventsAsync(cancellationToken))
            {
                _logger.LogWarning("Tiempo real: {IpKey} conectó pero no aceptó el registro de eventos.", Target.IpKey);
                await DisconnectQuietlyAsync();
                return false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tiempo real: error conectando a {IpKey}.", Target.IpKey);
            return false;
        }

        var previousAlive = LastAliveUtc;
        Touch();
        _connected = true;
        _logger.LogInformation("Tiempo real: escuchando {IpKey} (reloj {DeviceId}).", Target.IpKey, Target.DeviceId);
        Publish(new RealtimeConnectionStatus(true, previousAlive, previousAlive == null ? "Conectado" : "Reconectado"));
        return true;
    }

    private async Task<bool> HeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await _client.GetDeviceTimeAsync(cancellationToken) == null) return false;
            Touch();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tiempo real: falló el latido de {IpKey}.", Target.IpKey);
            return false;
        }
    }

    private async Task MarkDisconnectedAsync(string reason)
    {
        _connected = false;
        _logger.LogWarning("Tiempo real: {IpKey} sin conexión ({Reason}). Último contacto: {LastAlive:u}.", Target.IpKey, reason, LastAliveUtc);
        await DisconnectQuietlyAsync();
        Publish(new RealtimeConnectionStatus(false, LastAliveUtc, reason));
    }

    private async Task DisconnectQuietlyAsync()
    {
        try
        {
            await _client.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tiempo real: error al desconectar {IpKey}.", Target.IpKey);
        }
    }

    private void OnPunch(object? sender, RealtimePunch punch)
    {
        Touch();
        if (punch.IsInvalid)
        {
            _logger.LogDebug("Tiempo real: {IpKey} envió un marcaje inválido del usuario {UserId}; se ignora.", Target.IpKey, punch.UserId);
            return;
        }

        // El reloj puede repetir un marcaje (p. ej. al reconectar): sólo se publica la primera vez.
        lock (_recentGate)
        {
            var key = (punch.UserId, punch.CheckTime);
            if (!_recent.Add(key)) return;
            _recentOrder.Enqueue(key);
            if (_recentOrder.Count > RecentPunchCapacity) _recent.Remove(_recentOrder.Dequeue());
        }

        _channel.Publish(new RealtimeEvent(Target.DeviceId, DateTime.UtcNow, Punch: punch));
    }

    private void Publish(RealtimeConnectionStatus status) =>
        _channel.Publish(new RealtimeEvent(Target.DeviceId, DateTime.UtcNow, Connection: status));

    private void Touch() => Interlocked.Exchange(ref _lastAliveTicks, DateTime.UtcNow.Ticks);

    private TimeSpan ReconnectDelay(int failedAttempts)
    {
        var delays = _options.ReconnectDelays;
        return delays.Length == 0 ? TimeSpan.FromSeconds(30) : delays[Math.Min(failedAttempts, delays.Length - 1)];
    }

    /// <summary>
    /// Detiene la escucha y cierra la conexión. Espera un momento a que una sesión prestada la suelte; si no, la cierra igual.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _stop.Cancel();
        if (_loop != null)
        {
            try
            {
                await _loop;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tiempo real: la escucha de {IpKey} terminó con error.", Target.IpKey);
            }
        }
        _client.PunchReceived -= OnPunch;

        var locked = await _ipLock.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            var wasConnected = _connected;
            _connected = false;
            if (wasConnected) await DisconnectQuietlyAsync();
            _client.Dispose();
            Publish(new RealtimeConnectionStatus(false, LastAliveUtc, "Escucha detenida"));
        }
        finally
        {
            if (locked) _ipLock.Release();
            _stop.Dispose();
        }
        _logger.LogInformation("Tiempo real: escucha de {IpKey} detenida.", Target.IpKey);
    }
}
