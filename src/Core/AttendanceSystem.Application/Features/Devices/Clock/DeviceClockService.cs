using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Services;
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
/// del sistema operativo del servidor). Sincroniza hora y desfase, y detecta y corrige el desvío según la política
/// de la hora de los relojes (<see cref="PunchQuarantinePolicy"/>).
/// </summary>
public sealed class DeviceClockService
{
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeviceClockService> _logger;
    private readonly ISystemAlertRepository? _alerts;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly ISystemConfigurationRepository? _configuration;

    public DeviceClockService(
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver timeZoneResolver,
        ILogger<DeviceClockService>? logger = null,
        TimeProvider? timeProvider = null,
        ISystemAlertRepository? alerts = null,
        IUnitOfWork? unitOfWork = null,
        ISystemConfigurationRepository? configuration = null)
    {
        _configuration = configuration;
        _alerts = alerts;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver;
        _logger = logger ?? NullLogger<DeviceClockService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PunchQuarantinePolicy> PolicyAsync(CancellationToken cancellationToken = default) =>
        _configuration.GetClockPolicyAsync(cancellationToken);

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
    /// Lee la hora del reloj (ya conectado) y, si se desvía de la esperada en su zona más que la tolerancia de
    /// sincronización, lo resincroniza; si además pasa del umbral de alerta (o la resincronización falla), deja una
    /// alerta. Si el protocolo no permite leer la hora, no hace nada.
    /// </summary>
    /// <param name="alertDetail">Frase que se agrega a la alerta según el desvío (p. ej. qué pasó con los marcajes).</param>
    public async Task<DeviceClockCheck> VerifyAsync(IDeviceClient client, string timeZoneId, string deviceName, CancellationToken cancellationToken = default,
        string? deviceId = null, Func<TimeSpan, string?>? alertDetail = null)
    {
        var policy = await PolicyAsync(cancellationToken);
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
        if (drift.Duration() <= policy.ClockSyncTolerance) return new DeviceClockCheck(timeZoneId, expected, reported, drift, false);

        var corrected = await SyncAsync(client, timeZoneId, deviceName, cancellationToken);
        AttendanceTelemetry.DeviceClockCorrections.Add(1, new KeyValuePair<string, object?>("outcome", corrected ? "corrected" : "failed"));
        if (corrected && drift.Duration() <= policy.ClockDriftAlert)
        {
            _logger.LogInformation("Reloj {Device}: marcaba {Reported:yyyy-MM-dd HH:mm:ss} cuando en su zona {TimeZone} eran las {Expected:yyyy-MM-dd HH:mm:ss} (desvío {Drift}); se resincronizó sin alerta.",
                deviceName, reported, timeZoneId, expected, drift);
            return new DeviceClockCheck(timeZoneId, expected, reported, drift, corrected);
        }
        _logger.LogWarning(
            "ALERTA: el reloj {Device} marcaba {Reported:yyyy-MM-dd HH:mm:ss} cuando en su zona {TimeZone} eran las {Expected:yyyy-MM-dd HH:mm:ss} (desvío {Drift}). {Action}",
            deviceName, reported, timeZoneId, expected, drift, corrected ? "Se resincronizó." : "La resincronización falló; revisar el reloj.");
        if (_alerts != null && _unitOfWork != null && deviceId != null)
        {
            await _alerts.AddAsync(SystemAlert.Create(AlertType.DeviceClockDrift, deviceId,
                $"El reloj {deviceName} marcaba {reported:dd/MM/yyyy HH:mm} cuando en su zona eran las {expected:dd/MM/yyyy HH:mm}. " +
                (corrected ? "Se resincronizó." : "La resincronización falló; revise el reloj.") +
                (alertDetail?.Invoke(drift) is { Length: > 0 } detail ? " " + detail : "")), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return new DeviceClockCheck(timeZoneId, expected, reported, drift, corrected);
    }
}
