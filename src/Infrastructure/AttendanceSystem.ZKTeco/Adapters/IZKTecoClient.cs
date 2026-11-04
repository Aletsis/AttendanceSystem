using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.ZKTeco.Realtime;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.ZKTeco.Adapters;

/// <summary>
/// Conexión SDK a un reloj ZKTeco: las operaciones de <see cref="IDeviceClient"/> más los marcajes en tiempo real.
/// </summary>
public interface IZKTecoClient : IDeviceClient, IDisposable
{
    bool IsConnected { get; }

    /// <summary>Marcaje en tiempo real. Se dispara en un hilo del SDK.</summary>
    event EventHandler<RealtimePunch>? PunchReceived;

    /// <summary>Registra los eventos en el reloj (RegEvent); hay que repetirlo tras cada conexión.</summary>
    Task<bool> EnableRealtimeEventsAsync(CancellationToken cancellationToken = default);
}

public interface IZKTecoClientFactory
{
    IZKTecoClient Create();
}

public sealed class ZKTecoClientFactory : IZKTecoClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public ZKTecoClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IZKTecoClient Create() => new ZKTecoDeviceClient(_loggerFactory.CreateLogger<ZKTecoDeviceClient>());
}
