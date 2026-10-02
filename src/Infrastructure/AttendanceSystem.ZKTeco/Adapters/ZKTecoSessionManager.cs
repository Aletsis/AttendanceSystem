using AttendanceSystem.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AttendanceSystem.ZKTeco.Adapters;

public class ZKTecoSessionManager : IZKTecoSessionManager
{
    private class SessionRecord
    {
        public string SessionId { get; }
        public string IpKey { get; }
        public ZKTecoDeviceClient Client { get; }
        public SemaphoreSlim IpLock { get; }
        public DateTime LastAccessedUtc { get; set; }

        public SessionRecord(string sessionId, string ipKey, ZKTecoDeviceClient client, SemaphoreSlim ipLock)
        {
            SessionId = sessionId;
            IpKey = ipKey;
            Client = client;
            IpLock = ipLock;
            LastAccessedUtc = DateTime.UtcNow;
        }
    }

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ZKTecoSessionManager> _logger;
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _ipLocks = new();
    private readonly Timer _cleanupTimer;
    private bool _isDisposed;

    public ZKTecoSessionManager(ILoggerFactory loggerFactory, ILogger<ZKTecoSessionManager> logger)
    {
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

        // Si ya existía una sesión previa con este id, cerrarla antes de crear una nueva
        if (_sessions.TryRemove(sessionId, out var existingSession))
        {
            _logger.LogInformation("Cerrando sesión previa existente {SessionId}...", sessionId);
            await CloseSessionInternalAsync(existingSession, cancellationToken);
        }

        // Obtener o crear el semáforo para este dispositivo físico (IP:Puerto)
        var ipLock = _ipLocks.GetOrAdd(ipKey, _ => new SemaphoreSlim(1, 1));

        var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
        bool lockAcquired = await ipLock.WaitAsync(timeout, cancellationToken);
        if (!lockAcquired)
        {
            _logger.LogError("Tiempo de espera agotado al adquirir exclusión mutua para el dispositivo {IpKey}", ipKey);
            throw new TimeoutException($"El dispositivo {ipKey} está ocupado por otra operación.");
        }

        ZKTecoDeviceClient? client = null;
        try
        {
            client = new ZKTecoDeviceClient(_loggerFactory.CreateLogger<ZKTecoDeviceClient>());

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

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

    public Task<IDeviceClient> GetSessionAsync(string sessionId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.LastAccessedUtc = DateTime.UtcNow;
            return Task.FromResult<IDeviceClient>(session.Client);
        }

        _logger.LogWarning("No se encontró sesión activa con id {SessionId}.", sessionId);
        throw new InvalidOperationException($"No existe una sesión de dispositivo activa con el identificador '{sessionId}'. Debe conectar primero mediante ConnectDevice.");
    }

    public async Task<bool> DisconnectSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await CloseSessionInternalAsync(session, cancellationToken);
            return true;
        }

        return false;
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
    }

    private async Task CloseSessionInternalAsync(SessionRecord session, CancellationToken cancellationToken)
    {
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
            .Where(s => s.LastAccessedUtc < threshold)
            .ToList();

        foreach (var session in expiredSessions)
        {
            if (_sessions.TryRemove(session.SessionId, out _))
            {
                _logger.LogWarning("Sesión {SessionId} ({IpKey}) inactiva por más de 3 minutos. Cerrando automáticamente.", session.SessionId, session.IpKey);
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
