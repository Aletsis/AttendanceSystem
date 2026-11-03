using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;

namespace AttendanceSystem.Application.Features.Notifications;

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <param name="Href">Vista existente donde se atiende la notificación.</param>
public sealed record NotificationDto(string Id, string Title, string Message, DateTime TimestampUtc, NotificationSeverity Severity, string Href);

/// <summary>
/// Notificaciones de la barra superior: las alertas de sistema abiertas que el usuario puede atender, cada una con la
/// vista donde se atiende. Sólo se muestran las que llevan a una vista que el usuario puede abrir.
/// </summary>
/// <param name="Roles">
/// Roles del usuario. La campana consulta desde un ámbito propio (para no competir con la página por el mismo contexto
/// de datos) en el que no está el usuario del circuito; por eso los roles se pasan explícitamente.
/// </param>
public sealed record GetNotificationsQuery(IReadOnlyCollection<string> Roles) : IRequest<IReadOnlyList<NotificationDto>>;

public sealed class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    /// <summary>Las alertas de ausencia en puesto crítico no se resuelven: valen para el día en que se enviaron.</summary>
    public static readonly TimeSpan CriticalAbsenceLifetime = TimeSpan.FromHours(24);

    private readonly ISystemAlertRepository _alerts;
    private readonly TimeProvider _timeProvider;

    public GetNotificationsQueryHandler(ISystemAlertRepository alerts, TimeProvider? timeProvider = null)
    {
        _alerts = alerts;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private sealed record Route(string Title, string Href, string Permission, NotificationSeverity Severity);

    public async Task<IReadOnlyList<NotificationDto>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var active = (await _alerts.GetActiveAlertsAsync(cancellationToken))
            .Where(a => a.Type != AlertType.CriticalPositionAbsence || now - a.Timestamp <= CriticalAbsenceLifetime)
            .ToList();
        bool CanSee(string permission) => RolePermissions.Grants(request.Roles, permission);

        var notifications = new List<NotificationDto>();

        // Los horarios por corregir pueden ser muchos (uno por empleado): una sola notificación lleva a la lista del inicio.
        var schedules = active.Where(a => a.Type == AlertType.ScheduleIntegrity).ToList();
        if (schedules.Count > 0 && CanSee(Permissions.AttendanceView))
        {
            notifications.Add(new NotificationDto("horarios", "Horarios por corregir",
                schedules.Count == 1 ? schedules[0].Message : $"{schedules.Count} empleados o turnos tienen configuración que deja días sin turno.",
                schedules.Max(a => a.Timestamp), NotificationSeverity.Warning, "/"));
        }

        foreach (var alert in active.Where(a => a.Type != AlertType.ScheduleIntegrity))
        {
            var route = RouteOf(alert);
            if (route == null || !CanSee(route.Permission)) continue;
            notifications.Add(new NotificationDto(alert.Id.Value.ToString(), route.Title, alert.Message, alert.Timestamp, route.Severity, route.Href));
        }

        return notifications
            .OrderByDescending(n => n.Severity)
            .ThenByDescending(n => n.TimestampUtc)
            .ToList();
    }

    /// <summary>A qué vista lleva cada alerta y quién puede verla (el permiso de esa vista).</summary>
    private static Route? RouteOf(SystemAlert alert) => alert.Type switch
    {
        AlertType.CriticalPositionAbsence => new("Puesto crítico sin cubrir", "/attendance/logs", Permissions.AttendanceView, NotificationSeverity.Error),
        AlertType.DeviceOffline => new("Reloj desconectado", "/devices", Permissions.DevicesManage, NotificationSeverity.Error),
        AlertType.DeviceMemoryFull => new("Memoria de reloj casi llena", "/devices", Permissions.DevicesManage, NotificationSeverity.Warning),
        AlertType.DeviceClockDrift => new("Hora de reloj desfasada", "/devices/clock-time", Permissions.ClockAlertsView, NotificationSeverity.Warning),
        AlertType.PunchQuarantined => new("Marcajes en cuarentena", "/devices/clock-time", Permissions.ClockAlertsView, NotificationSeverity.Warning),
        AlertType.SystemError => SystemErrorRoute(alert.ReferenceId ?? ""),
        _ => null,
    };

    // Claves de OperationalAlerts: "tarea:{id de la tarea}:{fecha}", "relojes:{fecha}" y "cierre:{inicio del periodo}".
    private static Route SystemErrorRoute(string key) => key switch
    {
        _ when key.StartsWith("tarea:automated-database-backup:") => new("Falló el respaldo automático", "/backup", Permissions.BackupsManage, NotificationSeverity.Error),
        _ when key.StartsWith("tarea:automated-daily-report:") => new("Falló el reporte diario", "/settings", Permissions.SettingsManage, NotificationSeverity.Error),
        _ when key.StartsWith("tarea:download-attendance-daily:") => new("Falló la descarga automática", "/attendance/download", Permissions.DevicesDownload, NotificationSeverity.Error),
        _ when key.StartsWith("relojes:") => new("Relojes sin descargar", "/attendance/download", Permissions.DevicesDownload, NotificationSeverity.Error),
        _ when key.StartsWith("tarea:daily-attendance-calculation:") => new("Falló el cálculo del día anterior", "/attendance/calculation", Permissions.AttendanceCalculate, NotificationSeverity.Error),
        _ when key.StartsWith("tarea:auto-close-periods:") => new("Falló el cierre automático", "/attendance/periods", Permissions.AttendanceClosePeriod, NotificationSeverity.Error),
        _ when key.StartsWith("cierre:") => new("Cierre de periodo pospuesto", "/attendance/periods", Permissions.AttendanceClosePeriod, NotificationSeverity.Warning),
        _ => new("Alerta del sistema", "/settings", Permissions.SettingsManage, NotificationSeverity.Error),
    };
}
