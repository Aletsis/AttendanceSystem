using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.ZKTeco.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AttendanceSystem.ZKTeco.Adapters;

public class ZKTecoSessionManager : IZKTecoSessionManager
{
    private class SessionRecord
    {
        public string SessionId { get; }
        public string IpKey { get; }
        public IZKTecoClient Client { get; }
        public SemaphoreSlim IpLock { get; }
        /// <summary>Usa la conexión fija de la escucha en tiempo real: al cerrarse sólo suelta el candado.</summary>
        public bool Borrowed { get; }
        public DateTime LastAccessedUtc { get; set; }
        public int ActiveOperations;
        public bool IsClosing;

        public SessionRecord(string sessionId, string ipKey, IZKTecoClient client, SemaphoreSlim ipLock, bool borrowed = false)
        {
            SessionId = sessionId;
            IpKey = ipKey;
            Client = client;
            IpLock = ipLock;
            Borrowed = borrowed;
            LastAccessedUtc = DateTime.UtcNow;
            ActiveOperations = 0;
            IsClosing = false;
        }
    }

    private sealed class DeviceSessionLease : IDeviceSessionLease
    {
        private readonly SessionRecord _record;
        private int _disposed;

        public string SessionId => _record.SessionId;
        public IDeviceClient Client => _record.Client;

        public DeviceSessionLease(SessionRecord record)
        {
            _record = record;
            Interlocked.Increment(ref _record.ActiveOperations);
            _record.LastAccessedUtc = DateTime.UtcNow;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Decrement(ref _record.ActiveOperations);
            _record.LastAccessedUtc = DateTime.UtcNow;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private readonly IZKTecoClientFactory _clientFactory;
    private readonly ZKTecoRealtimeChannel _realtimeChannel;
    private readonly ZKTecoRealtimeOptions _realtimeOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ZKTecoSessionManager> _logger;
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _ipLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    // Escuchas en tiempo real por IP:Puerto; su conexión es la sesión fija de ese reloj.
    private readonly ConcurrentDictionary<string, ZKTecoRealtimeListener> _residents = new();
    private readonly SemaphoreSlim _residentsLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private bool _isDisposed;

    public ZKTecoSessionManager(
        IZKTecoClientFactory clientFactory,
        ZKTecoRealtimeChannel realtimeChannel,
        IOptions<ZKTecoRealtimeOptions> realtimeOptions,
        ILoggerFactory loggerFactory,
        ILogger<ZKTecoSessionManager> logger)
    {
        _clientFactory = clientFactory;
        _realtimeChannel = realtimeChannel;
        _realtimeOptions = realtimeOptions.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
        // Limpieza periódica de sesiones huérfanas o inactivas cada 1 minuto
        _cleanupTimer = new Timer(CleanupInactiveSessions, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<IDeviceClient> ConnectSessionAsync(
        string sessionId,
        string ipAddress,
        int port,
        int timeoutSeconds = 10,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var ipKey = $"{ipAddress}:{port}";
        _logger.LogInformation("Solicitando sesión {SessionId} para {IpKey}...", sessionId, ipKey);

        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);

        bool sessionLockAcquired = await sessionLock.WaitAsync(timeout, cancellationToken);
        if (!sessionLockAcquired)
        {
            _logger.LogError("Tiempo de espera agotado al adquirir exclusión para la sesión {SessionId}", sessionId);
            throw new TimeoutException($"La sesión {sessionId} está siendo configurada por otra operación.");
        }

        try
        {
            // Si ya existía una sesión previa con este id, cerrarla antes de crear una nueva
            if (_sessions.TryRemove(sessionId, out var existingSession))
            {
                _logger.LogInformation("Cerrando sesión previa existente {SessionId}...", sessionId);
                await CloseSessionInternalAsync(existingSession, cancellationToken);
            }

            // Obtener o crear el semáforo para este dispositivo físico (IP:Puerto)
            var ipLock = _ipLocks.GetOrAdd(ipKey, _ => new SemaphoreSlim(1, 1));

            bool ipLockAcquired = await ipLock.WaitAsync(timeout, cancellationToken);
            if (!ipLockAcquired)
            {
                _logger.LogError("Tiempo de espera agotado al adquirir exclusión mutua para el dispositivo {IpKey}", ipKey);
                throw new TimeoutException($"El dispositivo {ipKey} está ocupado por otra operación.");
            }

            IZKTecoClient? client = null;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                // Un reloj escuchado en tiempo real ya tiene su conexión: se presta en vez de abrir otra.
                if (_residents.TryGetValue(ipKey, out var resident))
                {
                    if (!await resident.EnsureConnectedAsync(cts.Token))
                    {
                        throw new InvalidOperationException($"No se pudo conectar al dispositivo ZKTeco en {ipAddress}:{port} (escuchado en tiempo real, sin conexión).");
                    }

                    _sessions[sessionId] = new SessionRecord(sessionId, ipKey, resident.Client, ipLock, borrowed: true);
                    _logger.LogInformation("Sesión {SessionId} usa la conexión de tiempo real de {IpKey}.", sessionId, ipKey);
                    return resident.Client;
                }

                client = _clientFactory.Create();

                bool connected = await client.ConnectAsync(ipAddress, port, cancellationToken: cts.Token);
                if (!connected)
                {
                    throw new InvalidOperationException($"No se pudo conectar al dispositivo ZKTeco en {ipAddress}:{port}. Verifique red y contraseña.");
                }

                var record = new SessionRecord(sessionId, ipKey, client, ipLock);
                _sessions[sessionId] = record;

                _logger.LogInformation("Sesión {SessionId} conectada exitosamente a {IpKey}.", sessionId, ipKey);
                return client;
            }
            catch
            {
                if (client != null)
                {
                    client.Dispose();
                }
                ipLock.Release();
                throw;
            }
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public Task<IDeviceClient> GetSessionAsync(string sessionId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_sessions.TryGetValue(sessionId, out var session) && !session.IsClosing)
        {
            session.LastAccessedUtc = DateTime.UtcNow;
            return Task.FromResult<IDeviceClient>(session.Client);
        }

        _logger.LogWarning("No se encontró sesión activa con id {SessionId}.", sessionId);
        throw new InvalidOperationException($"No existe una sesión de dispositivo activa con el identificador '{sessionId}'. Debe conectar primero mediante ConnectDevice.");
    }

    public Task<IDeviceSessionLease> AcquireSessionLeaseAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_sessions.TryGetValue(sessionId, out var session) && !session.IsClosing)
        {
            return Task.FromResult<IDeviceSessionLease>(new DeviceSessionLease(session));
        }

        _logger.LogWarning("No se pudo adquirir lease para sesión {SessionId} (inactiva o cerrándose).", sessionId);
        throw new InvalidOperationException($"No existe una sesión activa y disponible con el identificador '{sessionId}'.");
    }

    public async Task<bool> DisconnectSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                await CloseSessionInternalAsync(session, cancellationToken);
                return true;
            }

            return false;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task CloseAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cerrando todas las sesiones de dispositivos ZKTeco activas...");
        var activeSessions = _sessions.Values.ToList();
        _sessions.Clear();

        foreach (var session in activeSessions)
        {
            try
            {
                await CloseSessionInternalAsync(session, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cerrando sesión {SessionId} en {IpKey}", session.SessionId, session.IpKey);
            }
        }

        // Después de las sesiones, que pueden tener prestada la conexión de una escucha.
        await SetRealtimeDevicesAsync(Array.Empty<RealtimeDeviceTarget>(), CancellationToken.None);
    }

    public async Task<int> SetRealtimeDevicesAsync(IReadOnlyCollection<RealtimeDeviceTarget> devices, CancellationToken cancellationToken = default)
    {
        await _residentsLock.WaitAsync(cancellationToken);
        try
        {
            var desired = new Dictionary<string, RealtimeDeviceTarget>();
            foreach (var device in devices)
            {
                if (!desired.TryAdd(device.IpKey, device))
                {
                    _logger.LogWarning("Tiempo real: {IpKey} está configurado para más de un reloj; se escucha sólo como {DeviceId}.", device.IpKey, desired[device.IpKey].DeviceId);
                }
            }

            foreach (var (ipKey, listener) in _residents.ToArray())
            {
                if (desired.TryGetValue(ipKey, out var target) && target == listener.Target) continue;
                _residents.TryRemove(ipKey, out _);
                await listener.DisposeAsync();
            }

            foreach (var target in desired.Values.Where(t => !_residents.ContainsKey(t.IpKey)))
            {
                var listener = new ZKTecoRealtimeListener(
                    target,
                    _clientFactory.Create(),
                    _ipLocks.GetOrAdd(target.IpKey, _ => new SemaphoreSlim(1, 1)),
                    _realtimeChannel,
                    _realtimeOptions,
                    _loggerFactory.CreateLogger<ZKTecoRealtimeListener>());
                _residents[target.IpKey] = listener;
                listener.Start();
            }

            _logger.LogInformation("Tiempo real: {Count} relojes en escucha.", _residents.Count);
            return _residents.Count;
        }
        finally
        {
            _residentsLock.Release();
        }
    }

    public IReadOnlyList<RealtimeEvent> GetRealtimeStatuses()
    {
        var now = DateTime.UtcNow;
        return _residents.Values
            .Select(l => new RealtimeEvent(l.Target.DeviceId, now,
                Connection: new RealtimeConnectionStatus(l.IsConnected, l.LastAliveUtc, l.IsConnected ? "Conectado" : "Sin conexión")))
            .ToList();
    }

    private async Task CloseSessionInternalAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        session.IsClosing = true;

        // Esperar brevemente a que las operaciones activas en curso finalicen antes de disponer
        var waitDeadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref session.ActiveOperations) > 0 && DateTime.UtcNow < waitDeadline)
        {
            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (session.Borrowed)
        {
            _logger.LogInformation("Sesión {SessionId} devuelve la conexión de tiempo real de {IpKey}.", session.SessionId, session.IpKey);
            session.IpLock.Release();
            return;
        }

        try
        {
            _logger.LogInformation("Desconectando y liberando sesión {SessionId} ({IpKey})...", session.SessionId, session.IpKey);
            await session.Client.DisconnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al invocar DisconnectAsync en sesión {SessionId}", session.SessionId);
        }
        finally
        {
            session.Client.Dispose();
            session.IpLock.Release();
        }
    }

    private void CleanupInactiveSessions(object? state)
    {
        if (_isDisposed) return;

        var threshold = DateTime.UtcNow.AddMinutes(-3);
        var expiredSessions = _sessions.Values
            .Where(s => s.LastAccessedUtc < threshold && Volatile.Read(ref s.ActiveOperations) == 0 && !s.IsClosing)
            .ToList();

        foreach (var session in expiredSessions)
        {
            if (_sessions.TryRemove(session.SessionId, out _))
            {
                _logger.LogWarning("Sesión {SessionId} ({IpKey}) inactiva por más de 3 minutos sin operaciones activas. Cerrando automáticamente.", session.SessionId, session.IpKey);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CloseSessionInternalAsync(session, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al limpiar sesión inactiva {SessionId}", session.SessionId);
                    }
                });
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await _cleanupTimer.DisposeAsync();
        await CloseAllSessionsAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }
}
