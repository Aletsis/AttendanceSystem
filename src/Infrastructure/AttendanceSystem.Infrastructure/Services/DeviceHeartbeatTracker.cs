using System.Collections.Concurrent;
using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.Infrastructure.Services;

/// <summary>
/// Implementación Singleton basada en ConcurrentDictionary para el seguimiento en tiempo real
/// de la actividad de dispositivos sin sobrecargar la base de datos relacional.
/// </summary>
public class DeviceHeartbeatTracker : IDeviceHeartbeatTracker
{
    private static readonly TimeSpan DefaultOnlineTimeout = TimeSpan.FromSeconds(60);

    // Mapeo de identificadores (Número de serie o DeviceId en minúsculas) a la última fecha/hora UTC registrada
    private readonly ConcurrentDictionary<string, DateTime> _lastActivityByDevice = new(StringComparer.OrdinalIgnoreCase);

    // Caso 18: inicio de la comunicación sin interrupción (un hueco mayor que ConnectionGap la reinicia).
    private static readonly TimeSpan ConnectionGap = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTime> _connectedSince = new(StringComparer.OrdinalIgnoreCase);

    public void RecordActivity(string serialNumber, string? deviceId = null)
    {
        var now = DateTime.UtcNow;

        foreach (var key in new[] { serialNumber, deviceId }.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()))
        {
            if (!_lastActivityByDevice.TryGetValue(key, out var previous) || now - previous > ConnectionGap)
            {
                _connectedSince[key] = now;
            }
            _lastActivityByDevice[key] = now;
        }
    }

    public DateTime? GetConnectedSince(string? serialNumber, string? deviceId = null)
    {
        DateTime? since = null;
        foreach (var key in new[] { serialNumber, deviceId }.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()))
        {
            if (_connectedSince.TryGetValue(key, out var value) && (since == null || value < since)) since = value;
        }
        return since;
    }

    public DateTime? GetLastActivity(string? serialNumber, string? deviceId = null)
    {
        DateTime? activity = null;

        if (!string.IsNullOrWhiteSpace(serialNumber) && _lastActivityByDevice.TryGetValue(serialNumber.Trim(), out var bySn))
        {
            activity = bySn;
        }

        if (!string.IsNullOrWhiteSpace(deviceId) && _lastActivityByDevice.TryGetValue(deviceId.Trim(), out var byId))
        {
            if (activity == null || byId > activity.Value)
            {
                activity = byId;
            }
        }

        return activity;
    }

    public bool IsDeviceOnline(string? serialNumber, string? deviceId = null, TimeSpan? timeout = null)
    {
        var last = GetLastActivity(serialNumber, deviceId);
        if (last == null) return false;

        var threshold = timeout ?? DefaultOnlineTimeout;
        return (DateTime.UtcNow - last.Value) <= threshold;
    }
}
