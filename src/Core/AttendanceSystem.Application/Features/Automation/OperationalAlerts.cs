using System.Net;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Automation;

/// <summary>
/// Alertas de las tareas automáticas que requieren atención (una tarea que agotó sus reintentos, relojes que siguen sin
/// descargar, un cierre de periodo pospuesto). Cada una queda registrada como alerta de sistema y se avisa por correo a
/// los destinatarios de fallas del sistema una sola vez mientras siga abierta; se resuelve cuando la causa desaparece.
/// </summary>
public sealed class OperationalAlerts
{
    private const int MaxMessageLength = 500;

    private readonly ISystemAlertRepository _alerts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _email;
    private readonly ILogger<OperationalAlerts> _logger;

    public OperationalAlerts(ISystemAlertRepository alerts, IUnitOfWork unitOfWork, IEmailService email, ILogger<OperationalAlerts> logger)
    {
        _alerts = alerts;
        _unitOfWork = unitOfWork;
        _email = email;
        _logger = logger;
    }

    /// <summary>Registra la alerta y avisa; si ya está abierta sólo actualiza su texto. Devuelve si se abrió ahora.</summary>
    public async Task<bool> RaiseAsync(string key, string subject, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("ALERTA {Key}: {Message}", key, message);
        var stored = message.Length > MaxMessageLength ? message[..(MaxMessageLength - 1)] + "…" : message;

        var open = (await _alerts.GetActiveAlertsAsync(cancellationToken)).FirstOrDefault(a => a.Type == AlertType.SystemError && a.ReferenceId == key);
        if (open != null)
        {
            if (open.Message != stored)
            {
                open.UpdateMessage(stored);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return false;
        }

        await _alerts.AddAsync(SystemAlert.Create(AlertType.SystemError, key, stored), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _email.SendAlertAsync(subject, $"<p>{WebUtility.HtmlEncode(message)}</p>", AlertLevel.SystemFailure, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Queda registrada; el correo no se reintenta para no repetir el aviso.
            _logger.LogWarning(ex, "No se pudo enviar por correo la alerta {Key}.", key);
        }
        return true;
    }

    /// <summary>Resuelve las alertas abiertas cuya clave empieza con <paramref name="keyPrefix"/>.</summary>
    public async Task ResolveAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var open = (await _alerts.GetActiveAlertsAsync(cancellationToken))
            .Where(a => a.Type == AlertType.SystemError && a.ReferenceId != null && a.ReferenceId.StartsWith(keyPrefix, StringComparison.Ordinal))
            .ToList();
        if (open.Count == 0) return;

        foreach (var alert in open) alert.Resolve();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Alertas {Prefix}* resueltas: {Count}.", keyPrefix, open.Count);
    }
}
