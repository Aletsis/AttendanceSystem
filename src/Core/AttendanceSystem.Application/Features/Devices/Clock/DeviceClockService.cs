using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceSystem.Application.Features.Devices.Clock;

/// <summary>Resultado de comparar la hora del reloj con la esperada en su zona.</summary>
public sealed record DeviceClockCheck(string TimeZoneId, DateTime Expected, DateTime? Reported, TimeSpan? Drift, bool Corrected)
{
    public bool Verified => Reported.HasValue;
}

/// <summary>
/// Hora de un reloj checador en su propia zona horaria (la del dispositivo o, si no tiene, la del sistema; nunca la
/// del sistema operativo del servidor). Sincroniza hora y desfase, y detecta y corrige el desvío.
/// </summary>
public sealed class DeviceClockService
{
    /// <summary>Desvío tolerado antes de resincronizar el reloj.</summary>
    public static readonly TimeSpan DriftTolerance = TimeSpan.FromMinutes(2);

    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeviceClockService> _logger;

    public DeviceClockService(
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver timeZoneResolver,
        ILogger<DeviceClockService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver;
        _logger = logger ?? NullLogger<DeviceClockService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<string> TimeZoneOfAsync(DeviceId deviceId, CancellationToken cancellationToken = default)
        => _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);

    /// <summary>Hora civil que debería marcar ahora un reloj de la zona indicada.</summary>
    public DateTime ExpectedCivilNow(string timeZoneId)
        => DateTime.SpecifyKind(_timeNormalizer.ToCivilTime(_timeProvider.GetUtcNow().UtcDateTime, timeZoneId), DateTimeKind.Unspecified);

    /// <summary>Desfase actual de la zona respecto a UTC (incluye horario de verano si la zona lo tiene).</summary>
    public TimeSpan UtcOffsetOf(string timeZoneId)
        => _timeNormalizer.ResolveTimeZone(timeZoneId).GetUtcOffset(_timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Ajusta el reloj (ya conectado) a la hora actual de su zona, con su desfase.</summary>
    public async Task<bool> SyncAsync(IDeviceClient client, string timeZoneId, string deviceName, CancellationToken cancellationToken = default)
    {
        var civilNow = ExpectedCivilNow(timeZoneId);
        var offset = UtcOffsetOf(timeZoneId);
        if (offset.Minutes != 0)
        {
            _logger.LogWarning("La zona {TimeZone} del reloj {Device} tiene desfase de {Offset}: los relojes ADMS sólo admiten horas enteras.",
                timeZoneId, deviceName, offset);
        }

        var ok = await client.SetDeviceClockAsync(civilNow, offset, cancellationToken);
        _logger.LogInformation("Reloj {Device}: sincronización a {Time:yyyy-MM-dd HH:mm:ss} ({TimeZone}) {Result}.",
            deviceName, civilNow, timeZoneId, ok ? "aplicada" : "rechazada");
        return ok;
    }

    /// <summary>
    /// Lee la hora del reloj (ya conectado) y, si se desvía más de <see cref="DriftTolerance"/> de la esperada en su
    /// zona, lo resincroniza y deja una alerta. Si el protocolo no permite leer la hora, no hace nada.
    /// </summary>
    public async Task<DeviceClockCheck> VerifyAsync(IDeviceClient client, string timeZoneId, string deviceName, CancellationToken cancellationToken = default)
    {
        DateTime? reported;
        try
        {
            reported = await client.GetDeviceTimeAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo leer la hora del reloj {Device}.", deviceName);
            reported = null;
        }

        var expected = ExpectedCivilNow(timeZoneId);
        if (reported is null) return new DeviceClockCheck(timeZoneId, expected, null, null, false);

        var drift = reported.Value - expected;
        if (drift.Duration() <= DriftTolerance) return new DeviceClockCheck(timeZoneId, expected, reported, drift, false);

        var corrected = await SyncAsync(client, timeZoneId, deviceName, cancellationToken);
        AttendanceTelemetry.DeviceClockCorrections.Add(1, new KeyValuePair<string, object?>("outcome", corrected ? "corrected" : "failed"));
        _logger.LogWarning(
            "ALERTA: el reloj {Device} marcaba {Reported:yyyy-MM-dd HH:mm:ss} cuando en su zona {TimeZone} eran las {Expected:yyyy-MM-dd HH:mm:ss} (desvío {Drift}). {Action}",
            deviceName, reported, timeZoneId, expected, drift, corrected ? "Se resincronizó." : "La resincronización falló; revisar el reloj.");
        return new DeviceClockCheck(timeZoneId, expected, reported, drift, corrected);
    }
}
