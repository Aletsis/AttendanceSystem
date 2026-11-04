using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.ZKTeco.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.Services.ZKTecoRealtime;

/// <summary>
/// Se suscribe a los marcajes en tiempo real del servicio ZKTeco y le indica qué relojes escuchar: los activos por SDK
/// de marca ZKTeco. Se activa con <c>ZKTecoService:Realtime:Enabled</c>. Si el servicio o el stream se caen, reintenta
/// con espera creciente; lo marcado mientras tanto lo recupera la descarga de respaldo al volver.
/// Debe correr en un solo proceso (el servidor web): el servicio ZKTeco admite un único suscriptor.
/// </summary>
public sealed class ZKTecoRealtimeSubscriber : BackgroundService
{
    public const string EnabledKey = "ZKTecoService:Realtime:Enabled";

    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ZKTecoRealtimeDispatcher _dispatcher;
    private readonly ILogger<ZKTecoRealtimeSubscriber> _logger;

    public ZKTecoRealtimeSubscriber(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ZKTecoRealtimeDispatcher dispatcher,
        ILogger<ZKTecoRealtimeSubscriber> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>(EnabledKey))
        {
            _logger.LogInformation("Tiempo real por SDK desactivado ({Key}).", EnabledKey);
            return;
        }

        var flushing = FlushLoopAsync(stoppingToken);
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SubscribeAsync(() => failures = 0, stoppingToken);
                _logger.LogWarning("Tiempo real: el servicio ZKTeco cerró el stream; se vuelve a suscribir.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tiempo real: sin conexión con el servicio ZKTeco; se reintenta.");
            }

            try
            {
                await Task.Delay(RetryDelays[Math.Min(failures++, RetryDelays.Length - 1)], stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await flushing;
        await _dispatcher.FlushAsync(CancellationToken.None);
    }

    private async Task SubscribeAsync(Action onSubscribed, CancellationToken stoppingToken)
    {
        var url = _configuration["ZKTecoService:Url"] ?? "http://localhost:5001";
        var headers = new Metadata();
        var apiKey = _configuration["ZKTecoService:ApiKey"] ?? _configuration["ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey)) headers.Add("x-api-key", apiKey);

        // Canal propio: el cliente compartido tiene el tiempo límite de HttpClient (100 s), que cortaría el stream. Los
        // pings de HTTP/2 detectan un servicio caído aunque no lleguen marcajes.
        using var channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                EnableMultipleHttp2Connections = true
            }
        });
        var client = new ZKTecoService.ZKTecoServiceClient(channel);

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var devices = await SyncDevicesAsync(client, headers, null, streamCts.Token);

        using var call = client.SubscribeRealtime(new SubscribeRealtimeRequest(), headers, cancellationToken: streamCts.Token);
        _logger.LogInformation("Tiempo real: suscrito al servicio ZKTeco ({Url}), {Count} relojes en escucha.", url, devices.Count);
        onSubscribed();

        var refreshing = RefreshDevicesLoopAsync(client, headers, devices, streamCts.Token);
        try
        {
            await foreach (var update in call.ResponseStream.ReadAllAsync(streamCts.Token))
            {
                _dispatcher.Handle(update);
            }
        }
        finally
        {
            streamCts.Cancel();
            await refreshing;
        }
    }

    /// <summary>Envía la lista de relojes si cambió (o siempre, sin lista anterior). Devuelve la lista vigente.</summary>
    private async Task<IReadOnlyList<RealtimeDevice>> SyncDevicesAsync(
        ZKTecoService.ZKTecoServiceClient client, Metadata headers, IReadOnlyList<RealtimeDevice>? previous, CancellationToken cancellationToken)
    {
        IReadOnlyList<RealtimeDevice> devices;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
            devices = (await repository.GetActiveDevicesAsync(cancellationToken))
                .Where(d => d.DownloadMethod == DeviceDownloadMethod.Sdk && d.Brand == DeviceBrand.ZKTeco && !string.IsNullOrWhiteSpace(d.IpAddress))
                .Select(d => new RealtimeDevice { DeviceId = d.Id.Value, IpAddress = d.IpAddress.Trim(), Port = d.Port })
                .OrderBy(d => d.DeviceId, StringComparer.Ordinal)
                .ToList();
        }

        if (previous != null && previous.SequenceEqual(devices)) return previous;

        var response = await client.SetRealtimeDevicesAsync(new SetRealtimeDevicesRequest { Devices = { devices } }, headers, cancellationToken: cancellationToken);
        if (!response.Success) throw new InvalidOperationException($"El servicio ZKTeco no aceptó los relojes en tiempo real: {response.Message}");
        _logger.LogInformation("Tiempo real: {Message}.", response.Message);
        return devices;
    }

    /// <summary>Recoge los relojes que se agreguen, editen o desactiven mientras dura la suscripción.</summary>
    private async Task RefreshDevicesLoopAsync(
        ZKTecoService.ZKTecoServiceClient client, Metadata headers, IReadOnlyList<RealtimeDevice> devices, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(DeviceRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    devices = await SyncDevicesAsync(client, headers, devices, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Tiempo real: no se pudo actualizar la lista de relojes.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FlushLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        var sinceKeepAlive = TimeSpan.Zero;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _dispatcher.FlushAsync(stoppingToken);
                sinceKeepAlive += FlushInterval;
                if (sinceKeepAlive >= KeepAliveInterval)
                {
                    sinceKeepAlive = TimeSpan.Zero;
                    _dispatcher.KeepAlive();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
